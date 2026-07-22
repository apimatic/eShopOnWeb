using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> over a scripted <see cref="StubHttpMessageHandler"/>,
/// wired the same way the composition root wires it.
/// </summary>
public class MaxioClientBuilder
{
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        Environment = "US",
        ProductFamilyHandle = "eshop-subscribe",
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call"
    };

    public StubHttpMessageHandler Handler { get; } = new();

    public MaxioSettings Settings => _settings;

    public MaxioClientBuilder WithBaseUrl(string? baseUrl)
    {
        _settings.BaseUrl = baseUrl;

        return this;
    }

    public MaxioClientBuilder WithProductFamilyHandle(string? handle)
    {
        _settings.ProductFamilyHandle = handle;

        return this;
    }

    public MaxioBillingClient Build()
    {
        // The composition root sets BaseAddress from the resolved target; mirror that here.
        var httpClient = new HttpClient(Handler)
        {
            BaseAddress = new Uri(_settings.ResolveBaseUrl())
        };

        return new MaxioBillingClient(httpClient, Options.Create(_settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    /// <summary>Builds a client that has to resolve its own base address, as a direct construction would.</summary>
    public MaxioBillingClient BuildWithoutPresetBaseAddress()
    {
        var httpClient = new HttpClient(Handler);

        return new MaxioBillingClient(httpClient, Options.Create(_settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }
}
