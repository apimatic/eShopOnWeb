using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing client (mirrors how <c>CatalogSettings</c> is bound).
/// Only <see cref="ApiKey"/> is sensitive and it arrives through .NET user-secrets; the handles,
/// ids and <see cref="BaseUrl"/> are environment metadata.
/// </summary>
public class MaxioSettings
{
    /// <summary>The Maxio API key. Supplied through user-secrets — never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The subdomain of the Maxio site, e.g. <c>apimatic-hackathon</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region, <c>US</c> or <c>EU</c>. This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = US_ENVIRONMENT;

    /// <summary>
    /// An explicit outbound base URL. When set it wins over the subdomain-derived host, so the
    /// same build can be pointed at production, a dev/sandbox tenant, or a local mock server
    /// purely through configuration. Leave empty to derive the host from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string DefaultProductHandle { get; set; } = string.Empty;

    public string AlternateProductHandle { get; set; } = string.Empty;

    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>
    /// How the provider collects payment for new subscriptions. The demo plans require no payment
    /// method, so subscriptions are invoiced (<c>remittance</c>) rather than charged automatically —
    /// enrolling therefore needs no card capture. Set to <c>automatic</c> once payment profiles are
    /// captured at signup.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = REMITTANCE_COLLECTION;

    /// <summary>Invoice the customer; no payment method is required to enroll.</summary>
    public const string REMITTANCE_COLLECTION = "remittance";

    /// <summary>The configuration section every Maxio setting is bound from.</summary>
    public const string SECTION_NAME = "Maxio";

    public const string US_ENVIRONMENT = "US";
    public const string EU_ENVIRONMENT = "EU";

    private const string US_HOST_TEMPLATE = "https://{0}.chargify.com";
    private const string EU_HOST_TEMPLATE = "https://{0}.ebilling.maxio.com";

    /// <summary>
    /// Resolves the server this client talks to: an explicit <see cref="BaseUrl"/> verbatim, else
    /// the host derived from <see cref="Subdomain"/> in the configured region. This is the single
    /// place retargeting happens, so pointing at a mock is a configuration change, not a code change.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        var template = string.Equals(Environment, EU_ENVIRONMENT, StringComparison.OrdinalIgnoreCase)
            ? EU_HOST_TEMPLATE
            : US_HOST_TEMPLATE;

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
