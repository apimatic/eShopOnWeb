using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the <c>Maxio</c> configuration section using the following keys:
/// <c>ApiKey</c>, <c>Subdomain</c>, <c>ProductFamilyHandle</c> and (optionally) <c>BaseUrl</c>.
/// Values are supplied through environment variables / user secrets and are never committed.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Basic auth username). Bound from MAXIO_API_KEY.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain. Bound from MAXIO_SITE_SUBDOMAIN.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Region/environment used to derive the API host when <see cref="BaseUrl"/> is not set (US or EU).</summary>
    public string? EnvironmentName { get; set; }

    /// <summary>The product family whose plans are offered to shoppers. Bound from MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead of
    /// deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the <see cref="BaseUrl"/> override when present, otherwise
    /// the host templated in the Maxio OpenAPI server configuration for the configured region
    /// (US: https://{site}.chargify.com, EU: https://{site}.ebilling.maxio.com).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.Trim().TrimEnd('/');
        }

        // When no subdomain is configured the host is unusable; IsConfigured/ConfigurationError guard
        // against real calls. A placeholder keeps DI wiring (HttpClient base address) inert.
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return "https://maxio.invalid";
        }

        bool isEu = string.Equals(EnvironmentName, "EU", StringComparison.OrdinalIgnoreCase);
        string host = isEu
            ? $"https://{Subdomain!.Trim().TrimEnd('/')}.ebilling.maxio.com"
            : $"https://{Subdomain!.Trim().TrimEnd('/')}.chargify.com";
        return host;
    }

    /// <summary>
    /// True when all required settings are present so subscription endpoints can talk to Maxio.
    /// The rest of PublicApi still works when this is false; subscription endpoints then report 503.
    /// </summary>
    public bool IsConfigured => ConfigurationError is null;

    /// <summary>Returns a descriptive message when required settings are missing, else null.</summary>
    public string? ConfigurationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                return "Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable.";
            }

            if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            {
                return "Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.";
            }

            if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            {
                return "Maxio:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN environment variable (or configure Maxio:BaseUrl).";
            }

            return null;
        }
    }

    /// <summary>
    /// Throws with a descriptive message when required settings are missing. Used when the
    /// subscription capability is actually exercised so misconfiguration fails fast and clearly
    /// instead of surfacing confusing per-request errors.
    /// </summary>
    public void Validate()
    {
        string? error = ConfigurationError;
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }
    }
}
