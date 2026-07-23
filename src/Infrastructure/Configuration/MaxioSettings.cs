using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section (mirrors how <c>CatalogSettings</c> is bound).
/// </summary>
/// <remarks>
/// Only <see cref="ApiKey"/> is sensitive and it must arrive through .NET user-secrets or an
/// environment variable — never a file in the repository. Everything else is environment metadata.
/// </remarks>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Supplied through user-secrets / environment; never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the host when no explicit base URL is configured.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// The Maxio data-centre region — "US" or "EU". This is a different axis from the deployment
    /// target: which server is reached is controlled by <see cref="BaseUrl"/>.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Explicit outbound base URL. When set it is honoured verbatim and always wins over the
    /// subdomain-derived host, so the same build can be pointed at production, a dev/sandbox tenant,
    /// or a local mock server purely through configuration.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the subscribable plans and the metered component.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Handle of the default plan offered by the storefront.</summary>
    public string? DefaultProductHandle { get; set; }

    /// <summary>Handle of the alternate plan, used as the upgrade/downgrade target.</summary>
    public string? AlternateProductHandle { get; set; }

    /// <summary>Handle of the metered component usage is reported against.</summary>
    public string? MeteredComponentHandle { get; set; }

    /// <summary>
    /// How Maxio collects payment for new subscriptions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <c>remittance</c> so a subscription can be created without a payment method on
    /// file: Maxio issues an invoice instead of attempting to charge a card. That matches plans
    /// seeded with "requires payment method" off, and keeps card capture and 3-DS out of the flow.
    /// </para>
    /// <para>
    /// Valid values depend on the site's billing architecture — <c>remittance</c>, <c>automatic</c>
    /// or <c>prepaid</c> on Relationship Invoicing, and <c>invoice</c> or <c>automatic</c> on the
    /// legacy Statements architecture. Set <c>automatic</c> to charge a stored payment method.
    /// </para>
    /// </remarks>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The collection method to use, falling back to the no-card-required default.</summary>
    public string ResolvePaymentCollectionMethod()
        => string.IsNullOrWhiteSpace(PaymentCollectionMethod)
            ? DefaultPaymentCollectionMethod
            : PaymentCollectionMethod.Trim().ToLowerInvariant();

    /// <summary>Invoice-style collection, so no payment method is required to subscribe.</summary>
    public const string DefaultPaymentCollectionMethod = "remittance";

    /// <summary>True when the configured region is Maxio's EU data centre.</summary>
    public bool IsEuRegion =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound base URL. An explicit <see cref="BaseUrl"/> is honoured verbatim;
    /// only when it is absent is the host derived from <see cref="Subdomain"/> and the region.
    /// This is the single place retargeting (production / dev / mock) happens.
    /// </summary>
    /// <exception cref="BillingConfigurationException">
    /// Neither an explicit base URL nor a subdomain is configured, or the explicit value is not a
    /// well-formed absolute URL.
    /// </exception>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var explicitUrl = BaseUrl.Trim().TrimEnd('/');

            if (!Uri.TryCreate(explicitUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new BillingConfigurationException(
                    $"'{SectionName}:{nameof(BaseUrl)}' must be an absolute http(s) URL, but was '{BaseUrl}'.");
            }

            return explicitUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"Configure either '{SectionName}:{nameof(BaseUrl)}' or '{SectionName}:{nameof(Subdomain)}' so the integration knows which server to target.");
        }

        var template = IsEuRegion ? EuHostTemplate : UsHostTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }

    /// <summary>
    /// Validates that the settings needed to reach the provider at all are present.
    /// </summary>
    /// <exception cref="BillingConfigurationException">A required setting is missing.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                $"'{SectionName}:{nameof(ApiKey)}' is not configured. Set it with 'dotnet user-secrets set \"{SectionName}:{nameof(ApiKey)}\" <key>'.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                $"'{SectionName}:{nameof(ProductFamilyHandle)}' is not configured; the integration cannot resolve the plans to offer.");
        }

        if (string.IsNullOrWhiteSpace(MeteredComponentHandle))
        {
            throw new BillingConfigurationException(
                $"'{SectionName}:{nameof(MeteredComponentHandle)}' is not configured; usage cannot be metered.");
        }

        // Surfaces a bad BaseUrl / missing Subdomain at startup rather than on the first call.
        ResolveBaseUrl();
    }
}
