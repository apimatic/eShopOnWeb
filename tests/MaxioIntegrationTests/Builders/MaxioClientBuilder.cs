using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> over a stub transport, wired exactly the way the
/// composition root wires the real one: authentication handler in front, base address resolved
/// from the typed settings.
/// </summary>
public class MaxioClientBuilder
{
    public const string ApiKey = "test-api-key-do-not-log";
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const string MeteredComponentHandle = "api-call";

    private readonly MaxioSettings _settings = new()
    {
        ApiKey = ApiKey,
        Subdomain = "apimatic-hackathon",
        Environment = "US",
        BaseUrl = "http://localhost:8080",
        ProductFamilyHandle = ProductFamilyHandle,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = MeteredComponentHandle,
        // Keep the backoff out of the test's wall clock while still exercising the retry path.
        RetryBaseDelayMilliseconds = 1
    };

    public StubHttpMessageHandler Transport { get; } = new();

    public MaxioSettings Settings => _settings;

    public MaxioClientBuilder WithMaxRetryAttempts(int attempts)
    {
        _settings.MaxRetryAttempts = attempts;
        return this;
    }

    public MaxioBillingClient Build()
    {
        var options = Options.Create(_settings);
        var authentication = new MaxioAuthenticationHandler(new StaticOptionsMonitor<MaxioSettings>(_settings))
        {
            InnerHandler = Transport
        };

        var httpClient = new HttpClient(authentication)
        {
            BaseAddress = new Uri(_settings.ResolveBaseUrl())
        };

        return new MaxioBillingClient(httpClient, options);
    }
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
