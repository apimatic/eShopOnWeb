using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> over a stub transport, with the same settings shape the
/// application binds from configuration.
/// </summary>
public static class MaxioTestContext
{
    public const string FAMILY_HANDLE = "eshop-subscribe";
    public const string COMPONENT_HANDLE = "api-call";
    public const string PRO_HANDLE = "eshop-pro";
    public const string BASIC_HANDLE = "basic-plan";

    public static MaxioSettings Settings(string? baseUrl = null) => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        Environment = "US",
        BaseUrl = baseUrl,
        ProductFamilyHandle = FAMILY_HANDLE,
        DefaultProductHandle = PRO_HANDLE,
        AlternateProductHandle = BASIC_HANDLE,
        MeteredComponentHandle = COMPONENT_HANDLE
    };

    public static MaxioBillingClient CreateClient(StubHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        return new MaxioBillingClient(
            new HttpClient(handler),
            Options.Create(settings ?? Settings()),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }
}
