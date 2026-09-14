using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetBaseAddress()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Require(Subdomain, nameof(Subdomain))}.chargify.com"
            : BaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var result) ||
            (result.Scheme != Uri.UriSchemeHttps && result.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(result.UserInfo))
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL without embedded credentials.");
        }

        return result;
    }

    public void Validate()
    {
        Require(ApiKey, nameof(ApiKey));
        Require(ProductFamilyHandle, nameof(ProductFamilyHandle));
        _ = GetBaseAddress();
    }

    private static string Require(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new MaxioConfigurationException($"Maxio:{key} is not configured.");
        }

        return value;
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}
