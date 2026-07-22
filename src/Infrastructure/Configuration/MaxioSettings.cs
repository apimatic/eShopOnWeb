using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section (mirrors how <c>CatalogSettings</c> is bound).
/// </summary>
/// <remarks>
/// Only <see cref="ApiKey"/> is a secret and it must come from .NET user-secrets or an environment
/// variable — never from a file in the repository. Everything else is environment metadata.
/// </remarks>
public class MaxioSettings : ISubscriptionCatalogSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Supplied through user-secrets; never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, for example "cp-exp-3".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region, "US" or "EU". This is the hosting region, a separate axis
    /// from the deployment target selected by <see cref="BaseUrl"/>.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// An explicit outbound base URL. When set it wins over the subdomain-derived host, so the
    /// same build can be pointed at production, a dev/sandbox tenant, or a local mock server
    /// purely through configuration. Leave empty to derive the host from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// How Maxio collects payment for new subscriptions: "remittance" (invoice the customer),
    /// "automatic" (charge a stored payment method), "prepaid", or "invoice".
    /// </summary>
    /// <remarks>
    /// Defaults to remittance. With automatic collection Maxio refuses to create a subscription
    /// when the plan generates a balance and the customer has no payment method on file, which is
    /// exactly the case for a demo that deliberately captures no card.
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// The product family id last observed at the provider. Informational only: the integration
    /// always resolves the live id from <see cref="ProductFamilyHandle"/>, because the provider
    /// reassigns ids whenever the catalog is re-created.
    /// </summary>
    public int? ProductFamilyId { get; set; }

    /// <summary>Handle of the plan offered as the default subscribe target.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>Informational; see <see cref="ProductFamilyId"/>.</summary>
    public int? DefaultProductId { get; set; }

    /// <summary>Handle of the second plan, used as the upgrade/downgrade target.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>Informational; see <see cref="ProductFamilyId"/>.</summary>
    public int? AlternateProductId { get; set; }

    /// <summary>Handle of the metered component usage is reported against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>Informational; see <see cref="ProductFamilyId"/>.</summary>
    public int? MeteredComponentId { get; set; }

    string ISubscriptionCatalogSettings.ProductFamilyHandle => ProductFamilyHandle;

    string ISubscriptionCatalogSettings.DefaultPlanHandle => DefaultProductHandle;

    string ISubscriptionCatalogSettings.AlternatePlanHandle => AlternateProductHandle;

    string ISubscriptionCatalogSettings.MeteredComponentHandle => MeteredComponentHandle;

    /// <summary>True when the configured region is the EU data centre.</summary>
    public bool IsEuRegion =>
        string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound base URL. An explicit <see cref="BaseUrl"/> is honoured verbatim;
    /// only when it is absent is the host derived from <see cref="Subdomain"/> and the region.
    /// This is the single place retargeting between production, a dev tenant, and a local mock
    /// happens, so no caller ever hardcodes a host.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Neither an explicit base URL nor a subdomain is configured, or the explicit base URL is
    /// not a well-formed absolute HTTP(S) URL.
    /// </exception>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var explicitUrl = BaseUrl.Trim();

            if (!Uri.TryCreate(explicitUrl, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Maxio:BaseUrl must be an absolute http or https URL, but was '{BaseUrl}'.");
            }

            return explicitUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        var template = IsEuRegion ? EuHostTemplate : UsHostTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
