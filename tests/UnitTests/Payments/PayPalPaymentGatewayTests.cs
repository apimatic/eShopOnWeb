using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Payments;

/// <summary>
/// Tests the gateway's translation of PayPal responses using the SDK's own seam — a fake
/// <see cref="HttpMessageHandler"/> — so no real network call happens. The stub always answers the OAuth
/// token request; the API call is what each test controls.
/// </summary>
public class PayPalPaymentGatewayTests
{
    private const string TokenJson = """{"access_token":"fake-token","token_type":"Bearer","expires_in":3600}""";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _apiResponder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> apiResponder) =>
            _apiResponder = apiResponder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.Contains("oauth2/token"))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, TokenJson));
            }
            var response = _apiResponder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static PayPalPaymentGateway GatewayReturning(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder));
        var options = new PayPalServerSdkClientOptions
        {
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        };
        var client = new PayPalServerSdkClient(httpClient, options);
        var settings = Options.Create(new PayPalSettings
        {
            ClientId = "id",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = "USD",
            RequestTimeoutSeconds = 30
        });
        return new PayPalPaymentGateway(client, settings, NullLogger<PayPalPaymentGateway>.Instance);
    }

    [Fact]
    public async Task VaultCardAsync_maps_safe_display_details_from_the_token_response()
    {
        const string body =
            """{"id":"tok_123","payment_source":{"card":{"last_digits":"1111","brand":"VISA","expiry":"2028-05","name":"Demo User"}}}""";
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Created, body));

        var card = new CardDetails("4111111111111111", "2028-05", "123", "Demo User",
            null, null, null, null, null, "US");
        var outcome = await gateway.VaultCardAsync(card, "idem-1", CancellationToken.None);

        Assert.Equal("tok_123", outcome.VaultId);
        Assert.Equal("VISA", outcome.Brand);
        Assert.Equal("1111", outcome.Last4);
        Assert.Equal("2028-05", outcome.Expiry);
    }

    [Fact]
    public async Task RefundAsync_maps_a_422_to_a_caller_actionable_PaymentProcessingException()
    {
        const string error =
            """{"name":"UNPROCESSABLE_ENTITY","message":"Refund failed.","debug_id":"dbg1","details":[{"issue":"CAPTURE_FULLY_REFUNDED","description":"Capture already fully refunded."}]}""";
        var gateway = GatewayReturning(_ => Json((HttpStatusCode)422, error));

        var ex = await Assert.ThrowsAsync<PaymentProcessingException>(
            () => gateway.RefundAsync("capture-1", 5m, "USD", "idem-2", CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("refunded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefundAsync_maps_our_credential_failure_401_to_a_502()
    {
        // A 401 is OUR credentials, not the caller's fault — it must not pass through as a client error.
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Unauthorized,
            """{"name":"AUTHENTICATION_FAILURE","message":"bad creds","debug_id":"dbg2"}"""));

        var ex = await Assert.ThrowsAsync<PaymentProcessingException>(
            () => gateway.RefundAsync("capture-1", 5m, "USD", "idem-3", CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task RefundAsync_maps_a_transport_failure_to_a_504()
    {
        var gateway = GatewayReturning(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<PaymentProcessingException>(
            () => gateway.RefundAsync("capture-1", 5m, "USD", "idem-4", CancellationToken.None));

        Assert.Equal(504, ex.StatusCode);
    }
}
