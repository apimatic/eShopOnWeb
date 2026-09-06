using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Builds the Maxio integration over a stubbed transport, wired the same way the DI extension
/// wires it, so tests cover the client, the retry handler and the workflow together.
/// </summary>
internal static class MaxioTestHost
{
    public const string ProductFamilyHandle = "demo-subscriptions";

    internal static MaxioSubscriptionService CreateService(StubHttpMessageHandler transport,
        string productFamilyHandle = ProductFamilyHandle)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = productFamilyHandle
        };

        var client = CreateClient(transport, settings);

        return new MaxioSubscriptionService(
            client,
            Options.Create(settings),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    internal static MaxioApiClient CreateClient(StubHttpMessageHandler transport, MaxioSettings? settings = null)
    {
        settings ??= new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = ProductFamilyHandle
        };

        var retryHandler = new MaxioTransientFaultHandler(NullLogger<MaxioTransientFaultHandler>.Instance)
        {
            InnerHandler = transport
        };

        var httpClient = new HttpClient(retryHandler)
        {
            BaseAddress = settings.ResolveBaseAddress()
        };

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }
}
