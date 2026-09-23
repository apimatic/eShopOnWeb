using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

/// <summary>
/// Tests the gateway error boundary against the SDK's real HTTP seam (a stub HttpMessageHandler), asserting
/// that no SDK exception escapes: provider rejections become PaymentValidationException, server/transport
/// failures become PaymentGatewayException.
/// </summary>
public class PayPalGatewayTests
{
    private const string TokenBody =
        """{"access_token":"test-token","token_type":"Bearer","expires_in":32400,"scope":"","app_id":"x","nonce":"n"}""";

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public RoutingHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // The OAuth token request must succeed before the operation runs.
            if (request.RequestUri!.AbsolutePath.Contains("oauth2/token"))
                return Task.FromResult(Json(HttpStatusCode.OK, TokenBody));
            return Task.FromResult(Json(_status, _body));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private static PayPalGateway GatewayReturning(HttpStatusCode status, string body)
    {
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" },
        };
        var client = new PayPalServerSdkClient(new HttpClient(new RoutingHandler(status, body)), options);
        return new PayPalGateway(client, Substitute.For<ILogger<PayPalGateway>>(), "USD");
    }

    [Fact]
    public async Task Refund_Success_IsParsed()
    {
        var body = """{"id":"REFUND-9","status":"COMPLETED","amount":{"currency_code":"USD","value":"5.00"}}""";
        var gateway = GatewayReturning(HttpStatusCode.Created, body);

        var result = await gateway.RefundAsync("CAP-1", 5m, "USD", "req-1", default);

        Assert.Equal("REFUND-9", result.RefundId);
        Assert.Equal(RefundOutcome.Completed, result.Outcome);
        Assert.Equal(5m, result.Amount);
    }

    [Fact]
    public async Task Refund_ProviderRejection_BecomesValidationException()
    {
        var body = """{"name":"UNPROCESSABLE_ENTITY","message":"Refund amount too high","debug_id":"abc123"}""";
        var gateway = GatewayReturning(HttpStatusCode.UnprocessableEntity, body);

        var ex = await Assert.ThrowsAsync<PaymentValidationException>(() =>
            gateway.RefundAsync("CAP-1", 5m, "USD", "req-2", default));
        Assert.Contains("UNPROCESSABLE_ENTITY", ex.Message);
    }

    [Fact]
    public async Task Refund_ServerError_BecomesGatewayException()
    {
        var body = """{"name":"INTERNAL_SERVER_ERROR","message":"boom","debug_id":"z"}""";
        var gateway = GatewayReturning(HttpStatusCode.InternalServerError, body);

        await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            gateway.RefundAsync("CAP-1", null, "USD", "req-3", default));
    }
}
