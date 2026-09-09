using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

/// <summary>
/// Tests the PayPal gateway against a stub HttpMessageHandler (the SDK's test seam), so no network
/// call happens. The stub answers the OAuth token request, then a canned response per operation.
/// </summary>
public class PayPalPaymentGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private const string TokenJson = """{"access_token":"stub","token_type":"Bearer","expires_in":3600}""";

    private static PayPalPaymentGateway Gateway(Func<HttpRequestMessage, HttpResponseMessage> apiResponder)
    {
        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("oauth2/token")
                ? Json(HttpStatusCode.OK, TokenJson)
                : apiResponder(req));
        var client = new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions());
        return new PayPalPaymentGateway(client, Options.Create(new PayPalOptions { Currency = "USD" }), NullLogger<PayPalPaymentGateway>.Instance);
    }

    private static readonly AuthorizeCardRequest SampleRequest = new()
    {
        OrderReference = "42",
        CurrencyCode = "USD",
        Amount = 19.5m,
        Card = new CardDetails { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123" },
        IdempotencyKey = "auth-abc"
    };

    [Fact]
    public async Task Authorize_ReadsAuthorizationFromCreateResponse_WithoutSecondCall()
    {
        // CreateOrder returns the hold inline (card + AUTHORIZE auto-authorizes), so no /authorize call is needed.
        const string createJson = """
        {
          "id": "PPORDER1",
          "status": "COMPLETED",
          "purchase_units": [
            { "payments": { "authorizations": [
              { "id": "AUTH123", "status": "CREATED", "expiration_time": "2030-01-01T00:00:00Z" }
            ] } }
          ]
        }
        """;
        var gateway = Gateway(_ => Json(HttpStatusCode.Created, createJson));

        var result = await gateway.AuthorizeAsync(SampleRequest);

        Assert.Equal("PPORDER1", result.PayPalOrderId);
        Assert.Equal("AUTH123", result.AuthorizationId);
        Assert.Equal("CREATED", result.Status);
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), result.ExpiresAt);
    }

    [Fact]
    public async Task Authorize_On422_TranslatesToInvalidRequest_WithDebugIdAndIssue()
    {
        const string errorJson = """
        {
          "name": "UNPROCESSABLE_ENTITY",
          "message": "The requested action could not be performed.",
          "debug_id": "abc123debug",
          "details": [ { "issue": "TRANSACTION_REFUSED", "description": "The request was refused" } ]
        }
        """;
        var gateway = Gateway(_ => Json(HttpStatusCode.UnprocessableEntity, errorJson));

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.AuthorizeAsync(SampleRequest));

        Assert.Equal(PaymentGatewayErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal("abc123debug", ex.DebugId);
        Assert.Equal("TRANSACTION_REFUSED", ex.Issue);
        Assert.Contains("TRANSACTION_REFUSED", ex.Message);
    }

    [Fact]
    public async Task Authorize_On401_TranslatesToProviderError_NotCallerFault()
    {
        // 401 falls to the CreateOrder error's raw fallback (its typed body covers 400/401/422; here we
        // send a non-Error JSON so it lands on RawError with the status), which maps to a provider error.
        var gateway = Gateway(_ => Json(HttpStatusCode.Unauthorized, """{"foo":"bar"}"""));

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.AuthorizeAsync(SampleRequest));

        Assert.Equal(PaymentGatewayErrorKind.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task Void_On204NoContent_Succeeds()
    {
        // A successful void is 204 with no body; the gateway must treat the resulting empty-body
        // deserialization as success, not a provider error.
        var gateway = Gateway(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await gateway.VoidAsync("AUTH123", "void-abc"); // must not throw
    }

    [Fact]
    public async Task Refund_ReturnsRefundId()
    {
        const string refundJson = """{ "id": "REFUND9", "status": "COMPLETED", "amount": { "currency_code": "USD", "value": "5.00" } }""";
        var gateway = Gateway(_ => Json(HttpStatusCode.Created, refundJson));

        var result = await gateway.RefundAsync("CAP1", 5.00m, "USD", "rf-key");

        Assert.Equal("REFUND9", result.RefundId);
        Assert.Equal(5.00m, result.Amount);
        Assert.Equal("COMPLETED", result.Status);
    }
}
