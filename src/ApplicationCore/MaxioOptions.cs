using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are sourced from environment
/// variables / user-secrets at deploy/dev time - never hard-code them.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key, used as the HTTP Basic Auth username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. "cp-exp-4" for https://cp-exp-4.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override for the API base address. Used verbatim when set.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the base address to call: <see cref="BaseUrl"/> when provided, otherwise
    /// derived from <see cref="Subdomain"/> using Maxio's default site host.
    /// </summary>
    public string GetEffectiveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(Subdomain))
        {
            return $"https://{Subdomain}.chargify.com";
        }

        throw new InvalidOperationException(
            "Maxio is not configured. Set Maxio:BaseUrl or Maxio:Subdomain (see MAXIO_SITE_SUBDOMAIN).");
    }
}
