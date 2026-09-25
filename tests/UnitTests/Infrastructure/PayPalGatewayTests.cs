using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Services;
using PayPalServerSdk;
using PayPalServerSdk.Core.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

/// <summary>
/// Exercises the gateway's error boundary through the SDK's HttpClient seam (no real network): a
/// provider error becomes a PaymentException carrying the status/code, a connection failure becomes
/// a PaymentException, and a 204-No-Content void is treated as success.
/// </summary>
public class PayPalGatewayTests
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

    private static PayPalGateway GatewayReturning(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new PayPalServerSdkClient(new HttpClient(new StubHandler(responder)),
            new PayPalServerSdkClientOptions { Retry = RetryOptions.Disabled() });
        var settings = Options.Create(new PayPalSettings { Currency = "USD", ClientId = "x", ClientSecret = "y", Environment = "sandbox" });
        return new PayPalGateway(client, settings, NullLogger<PayPalGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Provider_error_becomes_PaymentException_with_status_and_code()
    {
        // First call in AuthorizeAsync is CreateOrder; a 422 there carries a typed Error body.
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.UnprocessableEntity,
            """{"name":"UNPROCESSABLE_ENTITY","message":"Invalid card","debug_id":"dbg-123"}"""));

        var ex = await Assert.ThrowsAsync<PaymentException>(() => gateway.AuthorizeAsync(
            new PaymentAuthorizationRequest(Guid.NewGuid(), 10m,
                new CardDetails("4111111111111111", "2030-01", "123"), null, "test"),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Equal("UNPROCESSABLE_ENTITY", ex.ProviderCode);
        Assert.Equal("dbg-123", ex.DebugId);
    }

    [Fact]
    public async Task Connection_failure_becomes_PaymentException()
    {
        var gateway = GatewayReturning(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            gateway.GetAuthorizationAsync("AUTH1", CancellationToken.None));

        Assert.Contains("unreachable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Void_returning_204_no_content_is_treated_as_success()
    {
        // A successful void returns 204 with an empty body, which the SDK cannot deserialize.
        var gateway = GatewayReturning(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await gateway.VoidAsync("AUTH1", "void-key", CancellationToken.None);

        Assert.Equal("VOIDED", result.Status);
    }
}
