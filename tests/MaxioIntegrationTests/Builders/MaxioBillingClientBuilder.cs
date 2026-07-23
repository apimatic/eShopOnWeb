using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> over a <see cref="MaxioApiStub"/>, wired exactly the
/// way the composition roots wire it — the client resolves its own base address and credentials
/// from settings, so those are exercised rather than bypassed.
/// </summary>
public class MaxioBillingClientBuilder
{
    public const string TEST_API_KEY = "test-api-key";
    public const string TEST_SUBDOMAIN = "apimatic-hackathon";
    public const string TEST_FAMILY_HANDLE = "eshop-subscribe";

    public MaxioApiStub Stub { get; } = new MaxioApiStub();

    private MaxioSettings _settings = new MaxioSettings
    {
        ApiKey = TEST_API_KEY,
        Subdomain = TEST_SUBDOMAIN,
        Environment = MaxioSettings.US_ENVIRONMENT,
        ProductFamilyHandle = TEST_FAMILY_HANDLE,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call"
    };

    public MaxioBillingClientBuilder WithSettings(MaxioSettings settings)
    {
        _settings = settings;
        return this;
    }

    public MaxioBillingClientBuilder WithoutProductFamilyHandle()
    {
        _settings.ProductFamilyHandle = string.Empty;
        return this;
    }

    public MaxioBillingClientBuilder WithBaseUrl(string baseUrl)
    {
        _settings.BaseUrl = baseUrl;
        return this;
    }

    public MaxioBillingClient Build()
    {
        return new MaxioBillingClient(new HttpClient(Stub), _settings);
    }
}
