using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> over a stubbed transport, wired exactly the way the
/// application wires it — through <see cref="MaxioBillingDependencies.CreateClientOptions"/> — so
/// the tests exercise the production configuration path rather than a parallel one.
/// </summary>
internal sealed class MaxioTestHarness : IDisposable
{
    private readonly HttpClient _httpClient;

    private MaxioTestHarness(StubHttpMessageHandler handler,
        HttpClient httpClient,
        MaxioSettings settings,
        RecordingLogger<MaxioBillingClient> logger,
        MaxioBillingClient client)
    {
        Handler = handler;
        _httpClient = httpClient;
        Settings = settings;
        Logger = logger;
        Client = client;
    }

    internal StubHttpMessageHandler Handler { get; }

    internal MaxioSettings Settings { get; }

    internal RecordingLogger<MaxioBillingClient> Logger { get; }

    internal MaxioBillingClient Client { get; }

    internal static MaxioTestHarness Create(Action<MaxioSettings>? configure = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = "test-site",
            Environment = "US",
            ProductFamilyHandle = "eshop-subscribe",
            DefaultProductHandle = "eshop-pro",
            AlternateProductHandle = "basic-plan",
            MeteredComponentHandle = "api-call"
        };

        configure?.Invoke(settings);

        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var sdkClient = new MaxioAdvancedBillingClient(httpClient, MaxioBillingDependencies.CreateClientOptions(settings));
        var logger = new RecordingLogger<MaxioBillingClient>();
        var client = new MaxioBillingClient(sdkClient, Options.Create(settings), logger);

        return new MaxioTestHarness(handler, httpClient, settings, logger, client);
    }

    /// <summary>
    /// Stubs the product-family lookup every plan read starts with, since Maxio's generated client
    /// cannot resolve a family by handle.
    /// </summary>
    internal MaxioTestHarness WithProductFamily()
    {
        Handler.Respond(HttpMethod.Get, "/product_families.json",
            System.Net.HttpStatusCode.OK, MaxioJson.ProductFamilyList());
        return this;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        Handler.Dispose();
    }
}
