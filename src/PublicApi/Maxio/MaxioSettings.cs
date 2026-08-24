using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Secrets are supplied via .NET user-secrets or environment variables, never from files in this repo.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the HTTP Basic username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "mysite" for https://mysite.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that contains the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var verbatim = BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/";
            return new Uri(verbatim, UriKind.Absolute);
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}
