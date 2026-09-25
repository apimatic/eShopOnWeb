using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

/// <summary>
/// Tests the PayPal gateway through the SDK's HttpClient seam (a stub handler), per dotnet-testing: no
/// network, the request/response contract and the failure translation are what we assert.
/// </summary>
public class PayPalGatewayTests
{
    // A stub that answers the OAuth token request and then the operation, routing by URL path.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _op;
        public List<string?> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> op) => _op = op;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content?.ReadAsStringAsync().Result);
            if (request.RequestUri!.AbsolutePath.EndsWith("/v1/oauth2/token"))
            {
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}"""));
            }
            var response = _op(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static (PayPalGateway gateway, StubHandler handler) Build(Func<HttpRequestMessage, HttpResponseMessage> op)
    {
        var handler = new StubHandler(op);
        var options = new PayPalServerSdkClientOptions
        {
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        };
        var client = new PayPalServerSdkClient(new HttpClient(handler), options);
        return (new PayPalGateway(client, NullLogger<PayPalGateway>.Instance), handler);
    }

    private static readonly CardDetails TestCard =
        new("4111111111111111", "2030-01", "123", "John Doe", null, null, null, "US", null);

    [Fact]
    public async Task Authorize_maps_paypal_ids_and_sends_amount_and_currency()
    {
        const string createResp = """{"id":"ORDER123","status":"CREATED"}""";
        const string authResp = """
        {"id":"ORDER123","status":"COMPLETED","purchase_units":[{"payments":{"authorizations":[
        {"id":"AUTH123","status":"CREATED","expiration_time":"2030-01-01T00:00:00Z",
        "amount":{"currency_code":"USD","value":"47.50"}}]}}]}
        """;
        var (gateway, handler) = Build(req =>
            req.RequestUri!.AbsolutePath.Contains("/authorize")
                ? StubHandler.Json(HttpStatusCode.Created, authResp)
                : StubHandler.Json(HttpStatusCode.Created, createResp));

        var result = await gateway.AuthorizeOrderAsync(
            "ESHOP-1-abc", 47.50m, "USD", PaymentInstrument.FromCard(TestCard), "ESHOP-1-abc", default);

        Assert.Equal("ORDER123", result.PayPalOrderId);
        Assert.Equal("AUTH123", result.AuthorizationId);
        Assert.Equal("CREATED", result.Status);
        // The create body must carry the exact amount and currency PayPal will hold.
        Assert.Contains(handler.Bodies, b => b is not null && b.Contains("\"value\":\"47.50\"") && b.Contains("\"currency_code\":\"USD\""));
    }

    [Fact]
    public async Task Authorize_with_payer_action_required_reports_challenge()
    {
        const string createResp = """{"id":"ORDER123","status":"CREATED"}""";
        // No authorization returned + PAYER_ACTION_REQUIRED = a browser challenge we must report, not round-trip.
        const string authResp = """{"id":"ORDER123","status":"PAYER_ACTION_REQUIRED","links":[{"href":"https://x","rel":"payer-action","method":"GET"}]}""";
        var (gateway, _) = Build(req =>
            req.RequestUri!.AbsolutePath.Contains("/authorize")
                ? StubHandler.Json(HttpStatusCode.Created, authResp)
                : StubHandler.Json(HttpStatusCode.Created, createResp));

        await Assert.ThrowsAsync<PaymentChallengeRequiredException>(() =>
            gateway.AuthorizeOrderAsync("inv", 10m, "USD", PaymentInstrument.FromCard(TestCard), "inv", default));
    }

    [Fact]
    public async Task Capture_maps_amount_fee_and_net()
    {
        const string captureResp = """
        {"id":"CAP1","status":"COMPLETED","amount":{"currency_code":"USD","value":"47.50"},
        "seller_receivable_breakdown":{"gross_amount":{"currency_code":"USD","value":"47.50"},
        "paypal_fee":{"currency_code":"USD","value":"1.72"},"net_amount":{"currency_code":"USD","value":"45.78"}}}
        """;
        var (gateway, _) = Build(_ => StubHandler.Json(HttpStatusCode.Created, captureResp));

        var result = await gateway.CaptureAsync("AUTH123", 47.50m, "USD", "inv", "cap-inv", default);

        Assert.Equal("CAP1", result.CaptureId);
        Assert.Equal(47.50m, result.CapturedAmount);
        Assert.Equal(1.72m, result.PayPalFee);
        Assert.Equal(45.78m, result.NetAmount);
    }

    [Fact]
    public async Task Void_with_204_no_content_is_treated_as_success()
    {
        // PayPal void returns 204 empty; the SDK cannot map it to PaymentAuthorization — must NOT be an error.
        var (gateway, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await gateway.VoidAsync("AUTH123", "void-inv", default); // no throw = pass
    }

    [Fact]
    public async Task Refund_error_400_becomes_caller_safe_PaymentException()
    {
        const string errorBody = """{"name":"UNPROCESSABLE_ENTITY","message":"nope","debug_id":"abc123"}""";
        var (gateway, _) = Build(_ => StubHandler.Json(HttpStatusCode.BadRequest, errorBody));

        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            gateway.RefundAsync("CAP1", 10m, "USD", "key-1", default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("UNPROCESSABLE_ENTITY", ex.Message); // provider code surfaced, no raw body leaked
    }

    [Fact]
    public async Task Connection_failure_becomes_provider_unavailable()
    {
        var (gateway, _) = Build(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            gateway.CaptureAsync("AUTH123", 10m, "USD", "inv", "cap-inv", default));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task Auth_credentials_rejected_becomes_provider_unavailable()
    {
        // Token endpoint refuses the client credentials → AuthSchemeException → 502 (our fault, not the caller's).
        var tokenFailHandler = new TokenFailHandler();
        var client = new PayPalServerSdkClient(new HttpClient(tokenFailHandler),
            new PayPalServerSdkClientOptions { Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "bad" } });
        var gateway = new PayPalGateway(client, NullLogger<PayPalGateway>.Instance);

        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            gateway.GetAuthorizationAsync("AUTH123", default));
        Assert.Equal(502, ex.StatusCode);
    }

    private sealed class TokenFailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"invalid_client"}""", Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
    }
}
