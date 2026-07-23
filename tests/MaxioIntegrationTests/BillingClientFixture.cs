using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> wired to a stub transport, so tests exercise the real
/// client — its request construction, its mapping, and its error translation — without live traffic.
/// </summary>
public static class BillingClientFixture
{
    public const string TestSubdomain = "test-site";
    public const string FamilyHandle = "eshop-subscribe";
    public const string DefaultPlanHandle = "eshop-pro";
    public const string AlternatePlanHandle = "basic-plan";
    public const string ComponentHandle = "api-call";

    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = TestSubdomain,
        Environment = "US",
        ProductFamilyHandle = FamilyHandle,
        DefaultProductHandle = DefaultPlanHandle,
        AlternateProductHandle = AlternatePlanHandle,
        MeteredComponentHandle = ComponentHandle
    };

    public static MaxioBillingClient Create(StubHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.ResolveBaseUrl())
        };

        return new MaxioBillingClient(
            httpClient,
            Options.Create(settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }
}
