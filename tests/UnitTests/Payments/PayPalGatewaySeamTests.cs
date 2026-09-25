using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;
using NSubstitute;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Payments;

/// <summary>
/// Tests the PayPal gateway against the SDK's HttpClient seam (a stub HttpMessageHandler), so no real
/// network calls happen. The OAuth token request is stubbed alongside the operation.
/// </summary>
public class PayPalGatewaySeamTests
{
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static PayPalGateway BuildGateway(Func<HttpRequestMessage, HttpResponseMessage> responder, out RoutingHandler handler)
    {
        handler = new RoutingHandler(responder);
        var client = new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions
        {
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        });
        return new PayPalGateway(client, Substitute.For<IAppLogger<PayPalGateway>>());
    }

    private static HttpResponseMessage RouteDefault(HttpRequestMessage req, Func<HttpRequestMessage, HttpResponseMessage> op)
    {
        if (req.RequestUri!.AbsolutePath.EndsWith("/oauth2/token", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
        }
        return op(req);
    }

    [Fact]
    public async Task VoidAsync_treats_204_no_content_as_success()
    {
        // PayPal returns 204 for a successful void; the SDK declares a PaymentAuthorization body, so the
        // empty body raises ResponseDeserializationException. The gateway must treat 2xx as success.
        var gateway = BuildGateway(req => RouteDefault(req, _ =>
            new HttpResponseMessage(HttpStatusCode.NoContent)), out var handler);

        // Should not throw.
        await gateway.VoidAsync("AUTH-123", "req-void", CancellationToken.None);

        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("/authorizations/AUTH-123/void"));
    }

    [Fact]
    public async Task CaptureAsync_translates_typed_422_to_caller_fault_gateway_exception()
    {
        const string errorBody = """
        {"name":"UNPROCESSABLE_ENTITY","message":"validation failed","debug_id":"dbg-xyz",
         "details":[{"issue":"AUTHORIZATION_EXPIRED","description":"expired"}]}
        """;

        var gateway = BuildGateway(req => RouteDefault(req, _ =>
            Json(HttpStatusCode.UnprocessableEntity, errorBody)), out _);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => gateway.CaptureAsync("AUTH-9", "req-cap", CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        Assert.True(ex.CallerFault);
        Assert.Equal("dbg-xyz", ex.DebugId);
        Assert.Contains("AUTHORIZATION_EXPIRED", ex.Message);
    }

    [Fact]
    public async Task ReauthorizeAsync_maps_422_to_not_renewable()
    {
        const string errorBody = """
        {"name":"UNPROCESSABLE_ENTITY","message":"too soon","debug_id":"dbg-1",
         "details":[{"issue":"REAUTHORIZATION_TOO_SOON"}]}
        """;

        var gateway = BuildGateway(req => RouteDefault(req, _ =>
            Json(HttpStatusCode.UnprocessableEntity, errorBody)), out _);

        await Assert.ThrowsAsync<AuthorizationNotRenewableException>(
            () => gateway.ReauthorizeAsync("AUTH-9", 10m, "USD", "req-re", CancellationToken.None));
    }

    [Fact]
    public async Task RefundAsync_returns_refund_id_and_amount()
    {
        const string body = """
        {"id":"RE-1","status":"COMPLETED","amount":{"currency_code":"USD","value":"10.00"}}
        """;

        var gateway = BuildGateway(req => RouteDefault(req, _ =>
            Json(HttpStatusCode.Created, body)), out _);

        var refund = await gateway.RefundAsync("CAP-1", 10m, "USD", "req-refund", note: null, CancellationToken.None);

        Assert.Equal("RE-1", refund.RefundId);
        Assert.Equal("COMPLETED", refund.Status);
        Assert.Equal(10.00m, refund.Amount);
    }
}
