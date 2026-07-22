using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Provider failures must reach the application as typed exceptions carrying enough detail to act
/// on — never as raw SDK exceptions, and never swallowed.
/// </summary>
public class MaxioBillingClientErrorHandling
{
    [Fact]
    public async Task BadCredentialsSurfaceAsAProviderErrorCarryingTheStatus()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathEndingWith("product_families.json"),
                HttpStatusCode.Unauthorized, "<html><body>Unauthorized</body></html>", "text/html");

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => harness.Client.ListPlansAsync());

        Assert.Equal(401, ex.StatusCode);
        // A 401 body is not necessarily JSON; it must still be reported, not crash the reader.
        Assert.Contains("Unauthorized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AServerErrorOnAWriteSurfacesAsProviderUnavailable()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub())
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("subscriptions.json"),
                HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => harness.Client.CreateSubscriptionAsync(55001, "eshop-pro"));

        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task ATransportFailureSurfacesAsProviderUnavailableRatherThanAnHttpException()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub())
            .Throw(HttpMethod.Post, MaxioApiStub.PathEndingWith("subscriptions.json"),
                new HttpRequestException("No such host is known."));

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => harness.Client.CreateSubscriptionAsync(55001, "eshop-pro"));

        Assert.Null(ex.StatusCode);
        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.Equal("CreateSubscriptionAsync", ex.Operation);
    }

    [Fact]
    public async Task EveryProviderFailureNamesTheOperationThatFailed()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("usages.json"),
                HttpStatusCode.UnprocessableEntity, MaxioJson.ErrorList("nope"));

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => harness.Client.RecordUsageAsync(88001, 3062733, 1m, null));

        Assert.Equal("RecordUsageAsync", ex.Operation);
    }

    [Fact]
    public async Task ACallerCancellationIsNotReportedAsAProviderFailure()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathEndingWith("product_families.json"),
                HttpStatusCode.OK, MaxioJson.ProductFamilyList());

        using var harness = new MaxioTestHarness(stub);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Cancelling is the caller's own doing; masking it as a billing outage would be wrong.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Client.ListPlansAsync(cts.Token));
    }

    [Fact]
    public void ConstructingTheClientWithoutAnApiKeyFailsWithAConfigurationError()
    {
        var settings = MaxioTestHarness.CreateSettings();
        settings.ApiKey = string.Empty;

        using var httpClient = new HttpClient(new MaxioApiStub());

        var ex = Assert.Throws<BillingConfigurationException>(
            () => new MaxioBillingClient(httpClient, Options.Create(settings)));

        Assert.Contains("Maxio:ApiKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructingTheClientWithNoTargetServerFailsFast()
    {
        var settings = new MaxioSettings { ApiKey = "k" };

        using var httpClient = new HttpClient(new MaxioApiStub());

        Assert.Throws<InvalidOperationException>(
            () => new MaxioBillingClient(httpClient, Options.Create(settings)));
    }
}
