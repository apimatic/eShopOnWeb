using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing provider. Bound from the <c>Maxio</c> configuration
/// section; the API key arrives through .NET user-secrets and never appears in source or
/// <c>appsettings.json</c>.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string CONFIG_SECTION = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Secret — supplied through user-secrets or an environment variable.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. <c>apimatic-hackathon</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio <b>data-centre region</b> — <c>US</c> or <c>EU</c>. This is a separate axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = UsRegion;

    /// <summary>
    /// Optional explicit outbound base URL. When set it <b>wins verbatim</b> over the
    /// subdomain-derived host, so the identical build can be pointed at production, a dev/sandbox
    /// tenant, or a local mock server purely through configuration. Leave empty to derive the host
    /// from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that scopes plan and component lookups.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// How the provider collects payment for new subscriptions. The demo enrolls without capturing a
    /// card, so the default bills the customer rather than auto-charging one — automatic collection
    /// would reject enrollment with "no payment method on file" for a non-zero opening balance.
    /// </summary>
    /// <remarks>
    /// Valid values depend on the Maxio site's billing architecture: <c>remittance</c> (Relationship
    /// Invoicing) or <c>invoice</c> (legacy Statements); <c>automatic</c> and <c>prepaid</c> are also
    /// accepted by the provider. Configurable so a site on the other architecture needs no code change.
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = DefaultPaymentCollectionMethod;

    /// <summary>The collection method used when none is configured.</summary>
    public const string DefaultPaymentCollectionMethod = "remittance";

    /// <summary>The wire value identifying the US data-centre region.</summary>
    public const string UsRegion = "US";

    /// <summary>The wire value identifying the EU data-centre region.</summary>
    public const string EuRegion = "EU";

    /// <summary>True when <see cref="Environment"/> selects the EU data centre.</summary>
    public bool IsEuropeanRegion => string.Equals(Environment, EuRegion, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when an explicit target server was configured.</summary>
    public bool HasExplicitBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>
    /// Resolves the single outbound base URL for the provider: the explicit <c>Maxio:BaseUrl</c> when
    /// one is configured, otherwise the host derived from <see cref="Subdomain"/> and the region.
    /// This is the only place the target server is decided.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Neither an explicit base URL nor a subdomain was configured, so no target can be determined.
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
                "The Maxio target server is not configured. Set either 'Maxio:BaseUrl' (an explicit host) " +
                "or 'Maxio:Subdomain' (from which the host is derived).");
        }

        var template = IsEuropeanRegion ? EuHostTemplate : UsHostTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
