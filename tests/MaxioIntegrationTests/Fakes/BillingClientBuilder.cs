using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> wired to a stub transport, so tests exercise the real
/// request building, response parsing and error translation without reaching the network.
/// </summary>
public static class BillingClientBuilder
{
    public const string TestApiKey = "test-api-key";
    public const string TestSubdomain = "example-site";
    public const string TestFamilyHandle = "eshop-subscribe";
    public const string TestComponentHandle = "api-call";

    public static MaxioSettings DefaultSettings() => new MaxioSettings
    {
        ApiKey = TestApiKey,
        Subdomain = TestSubdomain,
        Environment = "US",
        ProductFamilyHandle = TestFamilyHandle,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = TestComponentHandle
    };

    public static MaxioBillingClient Build(StubHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();

        var httpClient = new HttpClient(handler) { BaseAddress = settings.ResolveBaseUrl() };

        return new MaxioBillingClient(httpClient,
            Options.Create(settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    /// <summary>Builds a client whose base address is left for the client itself to resolve.</summary>
    public static MaxioBillingClient BuildWithoutBaseAddress(StubHttpMessageHandler handler,
        MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();

        return new MaxioBillingClient(new HttpClient(handler),
            Options.Create(settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }
}
