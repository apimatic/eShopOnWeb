using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Wires a real <see cref="MaxioBillingClient"/> over a stubbed transport, exactly as the composition root
/// wires it over a live one.
/// </summary>
public sealed class BillingTestContext : IDisposable
{
    public const string MockBaseUrl = "http://localhost:8080";

    private readonly HttpClient _httpClient;

    public BillingTestContext(MaxioSettings? settings = null)
    {
        Settings = settings ?? DefaultSettings();
        Handler = new StubHttpMessageHandler();
        _httpClient = Handler.CreateClient();
        Cache = new MaxioCatalogCache();
        Client = new MaxioBillingClient(_httpClient, Options.Create(Settings), Cache, new NullAppLogger<MaxioBillingClient>());
    }

    public StubHttpMessageHandler Handler { get; }

    public MaxioCatalogCache Cache { get; }

    public MaxioSettings Settings { get; }

    public MaxioBillingClient Client { get; }

    /// <summary>
    /// Settings that point at a local mock rather than a live tenant, so a test can never reach the
    /// network even if the stub were bypassed.
    /// </summary>
    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "cp-exp-2",
        Environment = "US",
        BaseUrl = MockBaseUrl,
        ProductFamilyHandle = "eshop-subscribe",
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call",
        CatalogCacheDuration = TimeSpan.Zero
    };

    /// <summary>Queues the two catalog reads the client performs when it resolves a component by handle.</summary>
    public BillingTestContext WithComponentLookup(string componentsJson = MaxioPayloads.MeteredComponents)
    {
        Handler.Enqueue(MaxioPayloads.ProductFamilies).Enqueue(componentsJson);
        return this;
    }

    /// <summary>Queues the two catalog reads the client performs when it lists or resolves plans.</summary>
    public BillingTestContext WithPlanLookup(string productsJson = MaxioPayloads.Products)
    {
        Handler.Enqueue(MaxioPayloads.ProductFamilies).Enqueue(productsJson);
        return this;
    }

    public void Dispose() => _httpClient.Dispose();
}

/// <summary>A logger that records nothing; these tests assert behaviour, not log output.</summary>
public sealed class NullAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args)
    {
    }

    public void LogWarning(string message, params object[] args)
    {
    }
}
