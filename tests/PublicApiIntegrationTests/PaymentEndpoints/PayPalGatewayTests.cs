using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Unit tests for the PayPal gateway using the SDK's own test seam — a fake <see cref="HttpMessageHandler"/>
/// on the client's <see cref="HttpClient"/> — so no real network calls happen. These lock in the two
/// behaviours found and fixed during end-to-end verification: invoice-scoped idempotency keys, and the
/// 204-No-Content void response; plus provider-error translation.
/// </summary>
[TestClass]
public class PayPalGatewayTests
{
    private const string TokenJson = """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""";

    private static PayPalGateway NewGateway(StubHandler handler)
    {
        var client = new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" },
        });
        var options = Options.Create(new PayPalOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = "USD",
        });
        return new PayPalGateway(client, options, NullLogger<PayPalGateway>.Instance);
    }

    [TestMethod]
    public async Task AuthorizeAsync_sends_invoice_scoped_idempotency_key_and_exact_amount()
    {
        const string orderJson = """
        {"id":"ORDER123","status":"COMPLETED","purchase_units":[{"payments":{"authorizations":[
        {"id":"AUTH123","status":"CREATED","amount":{"currency_code":"USD","value":"17.00"},
        "expiration_time":"2026-10-24T08:05:23Z"}]}}]}
        """;
        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("oauth2/token")
                ? Json(HttpStatusCode.OK, TokenJson)
                : Json(HttpStatusCode.Created, orderJson));

        var gateway = NewGateway(handler);
        var card = new CardDetails("4111111111111111", "2029-12", "123", "Demo Shopper", null);
        var result = await gateway.AuthorizeAsync(
            new AuthorizeInstruction(1, 17.00m, "ESHOP-1-abc123", card, null, null), CancellationToken.None);

        Assert.AreEqual("AUTH123", result.AuthorizationId);
        Assert.AreEqual("CREATED", result.Status);

        var createOrder = handler.Requests.Last(r => r.RequestUri!.AbsolutePath.EndsWith("/v2/checkout/orders"));
        Assert.IsTrue(createOrder.Headers.TryGetValues("PayPal-Request-Id", out var keys), "PayPal-Request-Id header must be present.");
        Assert.AreEqual("paypal-order-ESHOP-1-abc123", keys!.Single(), "Idempotency key must be namespaced by the unique invoice id.");

        var body = handler.BodyFor(createOrder);
        StringAssert.Contains(body, "\"value\":\"17.00\"", "The authorized amount must equal the order total to the cent.");
        StringAssert.Contains(body, "AUTHORIZE");
    }

    [TestMethod]
    public async Task RefundAsync_translates_422_to_PayPalIntegrationException_with_status_and_debug_id()
    {
        const string errorJson = """{"name":"UNPROCESSABLE_ENTITY","message":"business validation failed","debug_id":"dbg-1"}""";
        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("oauth2/token")
                ? Json(HttpStatusCode.OK, TokenJson)
                : Json(HttpStatusCode.UnprocessableEntity, errorJson));

        var gateway = NewGateway(handler);

        var ex = await Assert.ThrowsExceptionAsync<PayPalIntegrationException>(() =>
            gateway.RefundAsync(new RefundInstruction(1, "CAP123", 5.00m, "key-1", "ESHOP-1-abc123"), CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.AreEqual("dbg-1", ex.DebugId);
        Assert.IsFalse(ex.OutcomeUnknown);
    }

    [TestMethod]
    public async Task VoidAsync_treats_204_no_content_as_success()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            if (path.EndsWith("/void")) return new HttpResponseMessage(HttpStatusCode.NoContent); // empty body
            return Json(HttpStatusCode.OK, """{"id":"AUTH123","status":"VOIDED"}"""); // GetAuthorizedPayment confirm
        });

        var gateway = NewGateway(handler);
        var status = await gateway.VoidAsync(1, "ESHOP-1-abc123", "AUTH123", CancellationToken.None);

        Assert.AreEqual("VOIDED", status);
        Assert.IsTrue(handler.Requests.Any(r => r.RequestUri!.AbsolutePath.EndsWith("/void")), "The void call must be made.");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly Dictionary<HttpRequestMessage, string?> _bodies = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public string BodyFor(HttpRequestMessage request) => _bodies.TryGetValue(request, out var b) ? (b ?? "") : "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Buffer the body now; the SDK disposes request content per attempt.
            _bodies[request] = request.Content is null ? null : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
