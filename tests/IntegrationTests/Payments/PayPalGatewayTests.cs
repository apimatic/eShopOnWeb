using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// Exercises the PayPal gateway through the SDK's real serialization/deserialization by faking the
/// transport (an HttpMessageHandler), per the testing guidance. No real network calls.
/// </summary>
public class PayPalGatewayTests
{
    private const string TokenJson = """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""";

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static IPayPalGateway BuildGateway(Func<HttpRequestMessage, HttpResponseMessage> operationResponder)
    {
        var handler = new RoutingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/v1/oauth2/token"))
            {
                return Json(HttpStatusCode.OK, TokenJson);
            }
            return operationResponder(request);
        });

        var options = new PayPalOptions { ClientId = "id", ClientSecret = "secret", Environment = "sandbox", Currency = "USD" };
        var client = new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions
        {
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" },
            Environment = ServerEnvironment.Sandbox
        });
        return new PayPalGateway(client, Options.Create(options), NullLogger<PayPalGateway>.Instance);
    }

    [Fact]
    public async Task AuthorizeAsync_reads_the_authorization_from_the_order_response()
    {
        var order = """
        {"id":"ORDER123","status":"COMPLETED","purchase_units":[
          {"payments":{"authorizations":[{"id":"AUTH123","status":"CREATED","create_time":"2024-01-01T00:00:00Z"}]}}]}
        """;
        var gateway = BuildGateway(_ => Json(HttpStatusCode.Created, order));

        var outcome = await gateway.AuthorizeAsync(
            new PayPalAuthorizeRequest(36.50m, null, "eShopOrder-1", "pay-1",
                new PayPalCardInput("4111111111111111", "2030-01", "123", "Demo", null), null, "order 1"),
            default);

        Assert.Equal("ORDER123", outcome.PayPalOrderId);
        Assert.Equal("AUTH123", outcome.AuthorizationId);
        Assert.Equal("COMPLETED", outcome.OrderStatus);
        Assert.False(outcome.RequiresBuyerApproval);
    }

    [Fact]
    public async Task CaptureAsync_extracts_gross_fee_and_net()
    {
        var captured = """
        {"id":"CAP123","status":"COMPLETED","amount":{"currency_code":"USD","value":"36.50"},
         "seller_receivable_breakdown":{
           "gross_amount":{"currency_code":"USD","value":"36.50"},
           "paypal_fee":{"currency_code":"USD","value":"1.44"},
           "net_amount":{"currency_code":"USD","value":"35.06"}},
         "create_time":"2024-01-01T00:00:00Z"}
        """;
        var gateway = BuildGateway(_ => Json(HttpStatusCode.Created, captured));

        var outcome = await gateway.CaptureAsync("AUTH123", 36.50m, "cap-1", default);

        Assert.Equal("CAP123", outcome.CaptureId);
        Assert.Equal("COMPLETED", outcome.Status);
        Assert.Equal(36.50m, outcome.Gross);
        Assert.Equal(1.44m, outcome.Fee);
        Assert.Equal(35.06m, outcome.Net);
    }

    [Fact]
    public async Task RefundAsync_translates_a_provider_422_into_a_typed_gateway_exception()
    {
        var error = """
        {"name":"UNPROCESSABLE_ENTITY","message":"cannot refund","debug_id":"dbg-1",
         "details":[{"issue":"CAPTURE_FULLY_REFUNDED","description":"already refunded"}]}
        """;
        var gateway = BuildGateway(_ => Json(HttpStatusCode.UnprocessableEntity, error));

        var ex = await Assert.ThrowsAsync<PayPalGatewayException>(
            () => gateway.RefundAsync("CAP123", 10m, "refund-1", default));

        Assert.Equal(PayPalFailureKind.Conflict, ex.Kind);
        Assert.Equal("CAPTURE_FULLY_REFUNDED", ex.IssueCode);
        Assert.Equal("dbg-1", ex.DebugId);
    }

    [Fact]
    public async Task VoidAsync_treats_a_204_no_content_as_success()
    {
        var gateway = BuildGateway(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        // Should not throw despite the empty body.
        await gateway.VoidAsync("AUTH123", "void-1", default);
    }

    [Fact]
    public async Task AuthorizeAsync_flags_a_payer_action_required_challenge()
    {
        var order = """{"id":"ORDER9","status":"PAYER_ACTION_REQUIRED","purchase_units":[{}]}""";
        var gateway = BuildGateway(_ => Json(HttpStatusCode.Created, order));

        var outcome = await gateway.AuthorizeAsync(
            new PayPalAuthorizeRequest(10m, null, "eShopOrder-9", "pay-9",
                new PayPalCardInput("4111111111111111", "2030-01", "123", "Demo", null), null, null),
            default);

        Assert.True(outcome.RequiresBuyerApproval);
        Assert.Null(outcome.AuthorizationId);
    }
}
