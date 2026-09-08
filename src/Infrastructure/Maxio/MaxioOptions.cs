using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Secret values come from user-secrets / environment
/// variables and are never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (secret). Bound from MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "cp-exp-6". Bound from MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Site environment: US or EU. Bound from MAXIO_ENVIRONMENT.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>Handle of the product family that holds the subscription plans.
    /// Bound from MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base URL override; when set it is used as-is
    /// instead of deriving the host from the subdomain.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the API base address. <see cref="BaseUrl"/> wins verbatim when set.
    /// Otherwise US sites are derived from the subdomain; non-US sites must supply
    /// an explicit BaseUrl (the EU host shape is not assumed).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.Equals(Environment?.Trim(), "US", System.StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{Subdomain?.Trim()}.chargify.com";
        }

        throw new InvalidOperationException(
            $"Maxio base URL cannot be derived for environment '{Environment}'. " +
            $"Set the 'Maxio:{nameof(BaseUrl)}' configuration value (e.g. https://{Subdomain}.chargify.com).");
    }
}
