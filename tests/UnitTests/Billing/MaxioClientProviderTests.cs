using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing;

public class MaxioClientProviderTests
{
    [Fact]
    public void ReportsMisconfigurationAsUnavailableRatherThanCrashing()
    {
        var provider = Provider(new MaxioSettings { Subdomain = "cp-exp-2" }, out _);

        var ex = Assert.Throws<BillingNotConfiguredException>(() => provider.GetClient());

        Assert.Equal(503, ex.StatusCode);
        Assert.Contains("Maxio:ApiKey", ex.Message);
    }

    [Fact]
    public async Task DerivesTheBaseAddressFromTheConfiguredSubdomain()
    {
        var provider = Provider(MaxioTestHarness.Settings(), out var handler);

        await CallAsync(provider);

        Assert.Equal("cp-exp-2.chargify.com", handler.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task UsesAnExplicitBaseUrlVerbatimInsteadOfDerivingOne()
    {
        var provider = Provider(
            MaxioTestHarness.Settings(baseUrl: "https://maxio.internal.test/gateway"), out var handler);

        await CallAsync(provider);

        var uri = handler.Requests[0].RequestUri!;
        Assert.Equal("maxio.internal.test", uri.Host);
        Assert.StartsWith("/gateway/", uri.AbsolutePath);
    }

    [Fact]
    public void ReusesTheSameClientInstance()
    {
        var provider = Provider(MaxioTestHarness.Settings(), out _);

        Assert.Same(provider.GetClient(), provider.GetClient());
    }

    private static async Task CallAsync(IMaxioClientProvider provider) =>
        await provider.GetClient().Sites.ReadSite();

    private static MaxioClientProvider Provider(MaxioSettings settings, out StubHandler handler)
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "site", HttpStatusCode.OK, MaxioTestHarness.SiteJson);

        handler = new StubHandler(router.Respond);

        return new MaxioClientProvider(
            new StubHttpClientFactory(handler),
            new StaticOptionsMonitor<MaxioSettings>(settings),
            NullLogger<MaxioClientProvider>.Instance);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
