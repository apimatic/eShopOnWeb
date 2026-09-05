using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Binds to the "Maxio" configuration section. Values come from user-secrets/environment
/// in every environment - none of them are safe to hard-code, since the same build must be
/// able to target a different Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>The Maxio Advanced Billing API key (used as the Basic Auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "cp-exp-3" for https://cp-exp-3.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family that contains the subscribeable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving "https://{Subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseUri()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl
            : !string.IsNullOrWhiteSpace(Subdomain)
                ? $"https://{Subdomain}.chargify.com"
                : throw new InvalidOperationException(
                    $"Configure '{ConfigSectionName}:{nameof(Subdomain)}' or '{ConfigSectionName}:{nameof(BaseUrl)}' before using Maxio billing.");

        // HttpClient.BaseAddress must end in '/' for relative request URIs to combine correctly.
        return new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
