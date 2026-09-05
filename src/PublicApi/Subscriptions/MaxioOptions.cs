using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Configuration for the Maxio Advanced Billing site that owns subscriptions.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public Uri GetApiBaseAddress()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Require(Subdomain, nameof(Subdomain))}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }

        Require(ApiKey, nameof(ApiKey));
        Require(ProductFamilyHandle, nameof(ProductFamilyHandle));
        return new Uri(uri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static string Require(string value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new MaxioConfigurationException($"Maxio:{settingName} is required.");
        }

        return value;
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}
