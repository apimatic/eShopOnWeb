using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the <c>Maxio</c> configuration section.
/// </summary>
public class MaxioBillingOptions
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>
    /// Maxio API key (Basic auth username). Never hard-code; comes from configuration.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain (e.g. cp-exp-2).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the Maxio product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of one
    /// derived from the subdomain and hosting environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the base address of the Maxio API for the current configuration.
    /// Uses <see cref="BaseUrl"/> verbatim when provided; otherwise derives the
    /// Advanced Billing server URL from <see cref="Subdomain"/> and <paramref name="environment"/>
    /// ("US" hosts on *.chargify.com, "EU" hosts on *.ebilling.maxio.com).
    /// </summary>
    public string ResolveBaseUrl(string? environment)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: a subdomain (Maxio:Subdomain / MAXIO_SITE_SUBDOMAIN) or an explicit " +
                "base URL (Maxio:BaseUrl) is required.");
        }

        bool eu = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase);
        return eu
            ? $"https://{Subdomain.Trim().TrimEnd('/')}.ebilling.maxio.com"
            : $"https://{Subdomain.Trim().TrimEnd('/')}.chargify.com";
    }
}
