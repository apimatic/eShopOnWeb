using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section (mirrors how <see cref="CatalogSettings"/> is bound). Only <see cref="ApiKey"/> is
/// sensitive and must come from user-secrets; everything else is environment metadata.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string ConfigurationSection = "Maxio";

    /// <summary>The Maxio data-center region hosted in the United States.</summary>
    public const string UnitedStatesRegion = "US";

    /// <summary>The Maxio data-center region hosted in the European Union.</summary>
    public const string EuropeanUnionRegion = "EU";

    /// <summary>The API key used as the Basic-auth username (the password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, e.g. "apimatic-hackathon".</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// The Maxio data-center region, "US" or "EU". This is a different axis from the deployment
    /// target (production / dev / mock), which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// An explicit outbound base URL. When set it wins over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev tenant, or a local mock purely through configuration.
    /// Leave empty to derive the host from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The handle of the product family holding the plans and the metered component.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// The product family's numeric id. Optional — the client resolves the family from its handle,
    /// because Maxio reassigns numeric ids whenever the catalog is re-seeded.
    /// </summary>
    public int ProductFamilyId { get; set; }

    /// <summary>The handle of the plan the storefront subscribes to by default.</summary>
    public string? DefaultProductHandle { get; set; }

    /// <summary>The default plan's numeric id. Optional, for the same reason as <see cref="ProductFamilyId"/>.</summary>
    public int DefaultProductId { get; set; }

    /// <summary>The handle of the alternate plan, the target of an upgrade or downgrade.</summary>
    public string? AlternateProductHandle { get; set; }

    /// <summary>The alternate plan's numeric id. Optional.</summary>
    public int AlternateProductId { get; set; }

    /// <summary>The handle of the metered, pay-as-you-go component on the family.</summary>
    public string? MeteredComponentHandle { get; set; }

    /// <summary>The metered component's numeric id. Optional.</summary>
    public int MeteredComponentId { get; set; }

    /// <summary>
    /// The outbound base URL the billing client targets: the explicit <see cref="BaseUrl"/> verbatim
    /// when one is configured, otherwise the host derived from the subdomain and region.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"Neither '{ConfigurationSection}:BaseUrl' nor '{ConfigurationSection}:Subdomain' is configured, so the Maxio target server cannot be resolved.");
        }

        var subdomain = Subdomain!.Trim();

        return IsEuropeanUnionRegion
            ? $"https://{subdomain}.ebilling.maxio.com"
            : $"https://{subdomain}.chargify.com";
    }

    /// <summary>True when the configured region is the EU data centre; the US host is the default.</summary>
    public bool IsEuropeanUnionRegion =>
        string.Equals(Environment?.Trim(), EuropeanUnionRegion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How the product family is addressed in a request path: its numeric id when one is configured,
    /// otherwise the handle in Maxio's "handle:" form.
    /// </summary>
    public string ResolveProductFamilyReference()
    {
        if (ProductFamilyId > 0)
        {
            return ProductFamilyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                $"Neither '{ConfigurationSection}:ProductFamilyId' nor '{ConfigurationSection}:ProductFamilyHandle' is configured, so the plans cannot be listed.");
        }

        // Maxio addresses a family by handle with a "handle:" prefix on the id path segment.
        return $"handle:{Uri.EscapeDataString(ProductFamilyHandle!.Trim())}";
    }
}
