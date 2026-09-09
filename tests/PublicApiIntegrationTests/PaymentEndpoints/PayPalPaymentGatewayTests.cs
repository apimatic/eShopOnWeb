using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Unit tests for the PayPal gateway that fake the SDK's <see cref="HttpClient"/> seam, so no real
/// network calls happen. These do not use the web host, so they run independently of the integration
/// test server.
/// </summary>
[TestClass]
public class PayPalPaymentGatewayTests
{
    private const string TokenJson = """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var body = request.Content is null ? null : request.Content.ReadAsStringAsync().Result;
            Bodies.Add(body);
            var response = _responder(request, body ?? string.Empty);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public string? BodyFor(Func<HttpRequestMessage, bool> match)
        {
            for (var i = 0; i < Requests.Count; i++)
                if (match(Requests[i]))
                    return Bodies[i];
            return null;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static (PayPalPaymentGateway Gateway, StubHandler Handler) BuildGateway(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "test", ClientSecret = "test" }
        });
        var options = Options.Create(new PayPalOptions
        {
            ClientId = "test",
            ClientSecret = "test",
            Environment = "sandbox",
            Currency = "USD",
            RequestTimeoutSeconds = 30
        });
        return (new PayPalPaymentGateway(client, options, NullLogger<PayPalPaymentGateway>.Instance), handler);
    }

    private static bool IsCreateOrder(HttpRequestMessage r) =>
        r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/v2/checkout/orders");

    private static bool IsAuthorize(HttpRequestMessage r) =>
        r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/authorize");

    [TestMethod]
    public async Task AuthorizeAsync_SendsOrderTotalToTheCent_AndReturnsAuthorizationId()
    {
        var (gateway, handler) = BuildGateway((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            if (IsAuthorize(req))
                return Json(HttpStatusCode.Created, """
                    {"status":"COMPLETED","purchase_units":[{"payments":{"authorizations":[
                    {"id":"AUTH-123","status":"CREATED","amount":{"currency_code":"USD","value":"47.50"}}]}}]}
                    """);
            if (IsCreateOrder(req)) return Json(HttpStatusCode.Created, """{"id":"PPORDER-1","status":"CREATED"}""");
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var card = new CardDetails("4111111111111111", "2030-01", "123", "Test Buyer", null);
        var result = await gateway.AuthorizeAsync(
            new AuthorizeCommand(OrderId: 1, Amount: 47.50m, IdempotencyKey: "pay-1", Card: card, VaultId: null),
            CancellationToken.None);

        Assert.AreEqual("PPORDER-1", result.PayPalOrderId);
        Assert.AreEqual("AUTH-123", result.AuthorizationId);
        Assert.AreEqual("CREATED", result.Status);
        Assert.AreEqual(47.50m, result.AuthorizedAmount);

        // The amount sent to PayPal equals the order total to the cent.
        var createBody = handler.BodyFor(IsCreateOrder);
        StringAssert.Contains(createBody, "\"value\":\"47.50\"");
        StringAssert.Contains(createBody, "\"currency_code\":\"USD\"");
    }

    [TestMethod]
    public async Task AuthorizeAsync_RetriesTransient403NotAuthorized_ThenSucceeds()
    {
        var authorizeAttempts = 0;
        var (gateway, handler) = BuildGateway((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            if (IsCreateOrder(req)) return Json(HttpStatusCode.Created, """{"id":"PPORDER-1","status":"CREATED"}""");
            if (IsAuthorize(req))
            {
                authorizeAttempts++;
                if (authorizeAttempts == 1)
                    return Json(HttpStatusCode.Forbidden,
                        """{"name":"NOT_AUTHORIZED","message":"Authorization failed due to insufficient permissions.","debug_id":"d1"}""");
                return Json(HttpStatusCode.Created, """
                    {"status":"COMPLETED","purchase_units":[{"payments":{"authorizations":[
                    {"id":"AUTH-9","status":"CREATED"}]}}]}
                    """);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await gateway.AuthorizeAsync(
            new AuthorizeCommand(1, 10m, "pay-1", null, VaultId: "VAULT-1"),
            CancellationToken.None);

        Assert.AreEqual("AUTH-9", result.AuthorizationId);
        Assert.AreEqual(2, authorizeAttempts, "the transient 403 should have been retried once");
    }

    [TestMethod]
    public async Task RefundAsync_ReturnsRefundId()
    {
        var (gateway, _) = BuildGateway((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            if (req.RequestUri!.AbsolutePath.Contains("/refund"))
                return Json(HttpStatusCode.Created,
                    """{"id":"REFUND-77","status":"COMPLETED","amount":{"currency_code":"USD","value":"5.00"}}""");
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await gateway.RefundAsync(
            new RefundCommand(CaptureId: "CAP-1", Amount: 5.00m, IdempotencyKey: "refund-1", NoteToPayer: null),
            CancellationToken.None);

        Assert.AreEqual("REFUND-77", result.RefundId);
        Assert.AreEqual(5.00m, result.Amount);
    }

    [TestMethod]
    public async Task AuthorizeAsync_WhenPayerActionRequired_ThrowsChallengeRequired()
    {
        var (gateway, _) = BuildGateway((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            if (IsCreateOrder(req)) return Json(HttpStatusCode.Created, """{"id":"PPORDER-1","status":"CREATED"}""");
            if (IsAuthorize(req))
                return Json(HttpStatusCode.Created,
                    """{"status":"PAYER_ACTION_REQUIRED","links":[{"href":"https://x","rel":"payer-action","method":"GET"}]}""");
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var card = new CardDetails("4111111111111111", "2030-01", "123", "Test Buyer", null);
        await Assert.ThrowsExceptionAsync<PaymentChallengeRequiredException>(() =>
            gateway.AuthorizeAsync(new AuthorizeCommand(1, 10m, "pay-1", card, null), CancellationToken.None));
    }
}
