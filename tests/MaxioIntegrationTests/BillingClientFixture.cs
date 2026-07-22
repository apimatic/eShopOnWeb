using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> wired to a stubbed transport, exactly the way the composition
/// root wires the real one.
/// </summary>
public static class BillingClientFixture
{
    public const string ApiKey = "test-api-key-never-echoed";
    public const string BaseUrl = "http://localhost:8080";
    public const int ProductFamilyId = 3023074;

    public static MaxioBillingClient Create(HttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(settings.ResolveBaseUrl()) };

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey ?? string.Empty, Password = "x" }
        };
        options.Server.Production.Us.BaseUrl = settings.ResolveBaseUrl();

        var sdkClient = new MaxioAdvancedBillingClient(httpClient, options);

        return new MaxioBillingClient(sdkClient, settings, Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = ApiKey,
        Subdomain = "cp-exp-3",
        Environment = "US",
        BaseUrl = BaseUrl,
        ProductFamilyHandle = "eshop-subscribe",
        // Pre-resolved so a test exercising another operation does not also need the family lookup stubbed.
        ProductFamilyId = ProductFamilyId,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call"
    };
}
