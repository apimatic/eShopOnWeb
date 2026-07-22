using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ErrorHandling
{
    [Fact]
    public async Task EveryCallCarriesBasicAuthenticationBuiltFromTheApiKey()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ProPlanProductList);
        var client = BillingClientFixture.Create(handler);

        await client.ListPlansAsync();

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("Basic", sent.AuthScheme);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(sent.AuthParameter!));
        Assert.Equal($"{BillingClientFixture.ApiKey}:x", decoded);
    }

    [Fact]
    public async Task AnUnreachableProviderBecomesAServiceUnavailableTypedException()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(503, exception.StatusCode);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ATimedOutProviderBecomesAServiceUnavailableTypedException()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("timed out"));
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(15236915));

        Assert.Equal(503, exception.StatusCode);
    }

    [Fact]
    public async Task ACallerRequestedCancellationIsNotDisguisedAsAProviderFailure()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("cancelled"));
        var client = BillingClientFixture.Create(handler);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetSubscriptionAsync(15236915, cancellation.Token));
    }

    [Fact]
    public async Task TheApiKeyIsNeverIncludedInASurfacedErrorMessage()
    {
        // A hostile or misbehaving provider echoing the credential back must not leak it onwards.
        var handler = StubHttpMessageHandler.Always(
            $$"""{"errors":["rejected credential {{BillingClientFixture.ApiKey}}"]}""",
            HttpStatusCode.InternalServerError);

        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.DoesNotContain(BillingClientFixture.ApiKey, exception.Message);
        Assert.Contains("***", exception.Message);
    }

    [Fact]
    public async Task ASurfacedErrorMessageIsBoundedEvenWhenTheProviderBodyIsHuge()
    {
        var hugeBody = new string('x', 20_000);
        var handler = StubHttpMessageHandler.Always(hugeBody, HttpStatusCode.BadGateway);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.True(exception.Message.Length < 1_000,
            $"Expected a bounded message, got {exception.Message.Length} characters.");
    }

    [Fact]
    public async Task AProviderStatusIsCarriedThroughOntoTheDomainException()
    {
        var handler = StubHttpMessageHandler.Always("""{"error":"slow down"}""",
            HttpStatusCode.TooManyRequests);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(429, exception.StatusCode);
    }

    private class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw _exception;
        }
    }
}
