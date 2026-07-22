using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio</c>
/// configuration section (mirroring how <c>CatalogSettings</c> is bound).
/// <para>
/// Only <see cref="ApiKey"/> is a secret and it arrives through .NET user-secrets or an environment
/// variable — never from a file in the repository. Everything else is environment metadata.
/// </para>
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>The default host template for the US data-center region.</summary>
    private const string UsHostTemplate = "https://{0}.chargify.com";

    /// <summary>The default host template for the EU data-center region.</summary>
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Secret — supplied via user-secrets or environment only.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the host when no explicit override is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// The Maxio data-center region, <c>US</c> or <c>EU</c>. This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls. Unrecognised values fall back to US.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Explicit outbound target server. When set it is used <em>verbatim</em> and always wins over
    /// the <see cref="Subdomain"/>-derived host, so the identical build can be pointed at
    /// production, a dev/sandbox tenant, or a local mock purely through configuration
    /// (plan.md §2.3). Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The stable handle of the product family the plans and usage component live in.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// A previously observed numeric id for the product family. Only used when no handle is
    /// configured: the provider reassigns numeric ids on every re-seed, so handles are authoritative.
    /// </summary>
    public int? ProductFamilyId { get; set; }

    /// <summary>The handle of the primary plan.</summary>
    public string? DefaultProductHandle { get; set; }

    public int? DefaultProductId { get; set; }

    /// <summary>The handle of the alternate plan, used as the plan-change target.</summary>
    public string? AlternateProductHandle { get; set; }

    public int? AlternateProductId { get; set; }

    /// <summary>The handle of the metered component usage is recorded against.</summary>
    public string? MeteredComponentHandle { get; set; }

    public int? MeteredComponentId { get; set; }

    /// <summary>
    /// How the provider collects payment for new subscriptions: <c>remittance</c>, <c>automatic</c>,
    /// <c>invoice</c> or <c>prepaid</c>.
    /// <para>
    /// Defaults to <c>remittance</c> because this integration never captures a payment method — the
    /// storefront has no card-capture step (plan.md §1.3: the demo subscribes "without card capture
    /// or 3-DS"). Under <c>automatic</c> the provider tries to charge the first period immediately
    /// and refuses the enrolment outright when no payment profile exists. A deployment that does
    /// capture cards can set this to <c>automatic</c> without a code change.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>True when the configured region selects the EU data centre.</summary>
    public bool IsEuRegion =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when an explicit target server has been configured.</summary>
    public bool HasExplicitBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>
    /// The outbound base URL the provider client targets: the explicit <see cref="BaseUrl"/> when
    /// one is configured, otherwise the host derived from <see cref="Subdomain"/> and the region.
    /// This is the single place retargeting happens (plan.md §2.3 / §4.3).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Neither an explicit base URL nor a subdomain is configured, so no target can be determined.
    /// </exception>
    public string ResolveBaseUrl()
    {
        if (HasExplicitBaseUrl)
        {
            return BaseUrl!.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: set either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain'.");
        }

        var template = IsEuRegion ? EuHostTemplate : UsHostTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
