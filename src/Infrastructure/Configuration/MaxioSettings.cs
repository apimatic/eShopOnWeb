using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Only <see cref="ApiKey"/> is sensitive and it must arrive through user-secrets or the
/// environment — never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string CONFIG_SECTION = "Maxio";

    private const string US_HOST_TEMPLATE = "https://{0}.chargify.com";
    private const string EU_HOST_TEMPLATE = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Supplied through user-secrets / environment only.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain the derived host is built from.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region, "US" or "EU". This is a different axis from the deployment target,
    /// which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base URL. When set it wins outright over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant, or a local mock purely through
    /// configuration. Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-request timeout, in seconds, applied to every outbound billing call.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Handle of the product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Handle of the plan the storefront offers by default.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the alternate plan, used as the upgrade/downgrade target.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the metered component pay-as-you-go usage is reported against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>True when an explicit target server has been configured.</summary>
    public bool HasExplicitBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>True when the configured region is the European data centre.</summary>
    public bool IsEuropeanRegion => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound target server: an explicit <see cref="BaseUrl"/> if one is configured,
    /// otherwise the host derived from <see cref="Subdomain"/> and the region. This is the single place
    /// retargeting happens, so pointing the integration at another server is a configuration change and
    /// never a code change.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (HasExplicitBaseUrl)
        {
            return BaseUrl!.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Neither '{CONFIG_SECTION}:BaseUrl' nor '{CONFIG_SECTION}:Subdomain' is configured, so the billing provider's target server cannot be resolved.");
        }

        var template = IsEuropeanRegion ? EU_HOST_TEMPLATE : US_HOST_TEMPLATE;

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
