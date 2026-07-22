using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds a real <see cref="MaxioBillingClient"/> wired to a <see cref="MaxioApiStub"/>, configured
/// with the same handles the integration uses in the sandbox.
/// </summary>
public static class BillingClientFixture
{
    public const string ApiKey = "test-api-key";
    public const string Subdomain = "cp-exp-1";
    public const string FamilyHandle = "eshop-subscribe";
    public const string MeteredComponentHandle = "api-call";

    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = ApiKey,
        Subdomain = Subdomain,
        Environment = "US",
        ProductFamilyHandle = FamilyHandle,
        ProductFamilyId = 3026728,
        DefaultProductHandle = "eshop-pro",
        DefaultProductId = 7130993,
        AlternateProductHandle = "basic-plan",
        AlternateProductId = 7130994,
        MeteredComponentHandle = MeteredComponentHandle,
        MeteredComponentId = 3062731
    };

    public static MaxioBillingClient Create(MaxioApiStub stub, MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();

        var httpClient = new HttpClient(stub)
        {
            BaseAddress = new Uri(settings.ResolveBaseUrl())
        };

        return new MaxioBillingClient(httpClient,
            Options.Create(settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }
}
