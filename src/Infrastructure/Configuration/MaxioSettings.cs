using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the <c>Maxio</c>
/// configuration section (appsettings, environment variables, or user-secrets).
/// </summary>
/// <remarks>
/// Only <see cref="ApiKey"/> is sensitive and it must arrive through user-secrets or an
/// environment variable — never through a file in source control.
/// </remarks>
public class MaxioSettings : ISubscriptionCatalogSettings
{
    public const string ConfigurationSectionName = "Maxio";

    private const string UnitedStatesHostTemplate = "https://{0}.chargify.com";
    private const string EuropeanHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>Maxio API key. Secret — supplied through user-secrets or the environment.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. <c>cp-exp-4</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region, <c>US</c> or <c>EU</c>. This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base URL. When set it wins over the <see cref="Subdomain"/>-derived host,
    /// so the same build can be pointed at production, a dev/sandbox tenant, or a local mock server
    /// purely through configuration. Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public int ProductFamilyId { get; set; }

    /// <summary>Handle of the plan the storefront subscribes to by default.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    public int DefaultProductId { get; set; }

    /// <summary>Handle of the second plan, used as the upgrade/downgrade target.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    public int AlternateProductId { get; set; }

    /// <summary>Handle of the metered component pay-as-you-go usage accrues against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    public int MeteredComponentId { get; set; }

    /// <summary>
    /// How new subscriptions collect payment. The demo plans do not require a payment method, so
    /// <c>remittance</c> (invoice the customer) lets a subscribe complete without card capture.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>True when the region is the European data centre.</summary>
    public bool IsEuropeanRegion =>
        string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when an explicit <see cref="BaseUrl"/> override has been configured.</summary>
    public bool HasExplicitBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>
    /// Resolves the outbound target server. An explicit <see cref="BaseUrl"/> always wins; only when
    /// it is absent is the host derived from <see cref="Subdomain"/> and the region. This is the one
    /// place retargeting happens, so switching between production, a dev tenant, and a local mock is
    /// a configuration change and never a code change.
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
                "Maxio is not configured: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        var template = IsEuropeanRegion ? EuropeanHostTemplate : UnitedStatesHostTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }

    /// <summary>
    /// Resolves the target server without throwing, so composition roots can wire the integration up
    /// even in an environment where Maxio has not been configured at all.
    /// </summary>
    public bool TryResolveBaseUrl(out string baseUrl)
    {
        if (!HasExplicitBaseUrl && string.IsNullOrWhiteSpace(Subdomain))
        {
            baseUrl = string.Empty;
            return false;
        }

        baseUrl = ResolveBaseUrl();
        return Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute);
    }
}
