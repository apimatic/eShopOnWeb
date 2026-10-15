using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Returns the first problem found as a message naming the config key (never a value), or null.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return $"{CONFIG_NAME}:ApiKey is not configured.";
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) return $"{CONFIG_NAME}:ProductFamilyHandle is not configured.";
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain)) return $"{CONFIG_NAME}:Subdomain is not configured (and {CONFIG_NAME}:BaseUrl is not set).";
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"{CONFIG_NAME}:BaseUrl is not an absolute http(s) URL.";
        }
        return null;
    }
}
