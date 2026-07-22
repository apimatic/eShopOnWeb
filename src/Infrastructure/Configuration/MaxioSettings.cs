using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio billing integration, bound from the <c>Maxio</c> configuration
/// section. Only <see cref="ApiKey"/> is sensitive and it is supplied through user-secrets or
/// environment variables — never through a file in this repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string ConfigurationSection = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Secret — supplied out of band.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. <c>cp-exp-2</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region, <c>US</c> or <c>EU</c>. This is a separate axis from the
    /// deployment target, which is controlled by <see cref="BaseUrl"/>.
    /// </summary>
    public string Environment { get; set; } = MaxioRegion.Us;

    /// <summary>
    /// Optional explicit outbound base URL. When set it wins verbatim over the subdomain-derived host,
    /// so the same build can be pointed at production, a dev/sandbox tenant, or a local mock server
    /// purely through configuration. Leave empty to derive the host from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Handle of the primary plan offered on the storefront.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the alternate plan, the target of an upgrade or downgrade.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the metered component pay-as-you-go usage is recorded against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>How long the resolved catalog identifiers stay cached in-process.</summary>
    public int CatalogCacheMinutes { get; set; } = 30;

    /// <summary>Per-attempt outbound timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>True when an explicit target server has been configured.</summary>
    public bool HasExplicitBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>True when the configured region is the EU data centre.</summary>
    public bool IsEuropeanRegion =>
        string.Equals(Environment?.Trim(), MaxioRegion.Eu, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound base URL. An explicit <see cref="BaseUrl"/> is honoured verbatim; only
    /// when it is absent is the host derived from <see cref="Subdomain"/> and the region. This is the
    /// single place the target server is decided, so retargeting prod / dev / mock never leaks beyond
    /// this class.
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
                $"Maxio is not configured: set either '{ConfigurationSection}:{nameof(BaseUrl)}' or '{ConfigurationSection}:{nameof(Subdomain)}'.");
        }

        var template = IsEuropeanRegion ? EuHostTemplate : UsHostTemplate;
        return string.Format(CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }

    /// <summary>
    /// Fails fast on a configuration that cannot possibly work, so a misconfigured host surfaces at
    /// startup rather than as a confusing provider error on the first customer request.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: '{ConfigurationSection}:{nameof(ApiKey)}' is missing. Supply it through user-secrets or an environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: '{ConfigurationSection}:{nameof(ProductFamilyHandle)}' is missing.");
        }

        if (string.IsNullOrWhiteSpace(MeteredComponentHandle))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: '{ConfigurationSection}:{nameof(MeteredComponentHandle)}' is missing.");
        }

        // Throws when neither an explicit base URL nor a subdomain is present.
        var resolved = ResolveBaseUrl();

        if (!Uri.TryCreate(resolved, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: the resolved base URL '{resolved}' is not an absolute URI.");
        }
    }
}

/// <summary>The Maxio data-centre regions this integration understands.</summary>
public static class MaxioRegion
{
    public const string Us = "US";
    public const string Eu = "EU";
}
