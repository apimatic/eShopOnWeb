using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the <c>Maxio</c>
/// configuration section (plan.md §2.3, §5). Mirrors how <c>CatalogSettings</c> is bound.
/// </summary>
/// <remarks>
/// Only <see cref="ApiKey"/> is sensitive and it must come from .NET user-secrets or an
/// environment variable — never from a file in the repository. Everything else is environment
/// metadata.
/// </remarks>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    private const string UsBaseUrlTemplate = "https://{0}.chargify.com";
    private const string EuBaseUrlTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Supplied as the Basic-auth username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the host when no explicit base URL is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// The Maxio data-centre region, <c>US</c> or <c>EU</c>. This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// An explicit outbound base URL. When set it wins over the subdomain-derived host, so the
    /// same build can be pointed at production, a dev/sandbox tenant, or a local mock purely
    /// through configuration (plan.md §2.3).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The product family that scopes every plan and component this integration uses.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// The product family's numeric id, if known. Purely informational: the family is always
    /// resolved from <see cref="ProductFamilyHandle"/> at runtime because Maxio reassigns numeric
    /// ids whenever the catalog is re-created.
    /// </summary>
    public int? ProductFamilyId { get; set; }

    /// <summary>The plan the storefront offers as its primary option.</summary>
    public string? DefaultProductHandle { get; set; }

    /// <summary>Informational only; the plan is resolved from its handle.</summary>
    public int? DefaultProductId { get; set; }

    /// <summary>The plan customers may switch to.</summary>
    public string? AlternateProductHandle { get; set; }

    /// <summary>Informational only; the plan is resolved from its handle.</summary>
    public int? AlternateProductId { get; set; }

    /// <summary>The metered component pay-as-you-go usage is recorded against.</summary>
    public string? MeteredComponentHandle { get; set; }

    /// <summary>Informational only; the component is resolved from its handle.</summary>
    public int? MeteredComponentId { get; set; }

    /// <summary>True when <see cref="Environment"/> selects the EU data centre.</summary>
    public bool IsEuRegion => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The single place the outbound target is decided: an explicit <c>Maxio:BaseUrl</c> is used
    /// verbatim; otherwise the host is derived from <c>Maxio:Subdomain</c> and the region.
    /// </summary>
    /// <exception cref="BillingConfigurationException">
    /// Neither an explicit base URL nor a subdomain was configured, or the explicit value is not a
    /// well-formed absolute URL.
    /// </exception>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var explicitUrl = BaseUrl.Trim();

            // The scheme check matters: Uri.TryCreate accepts "localhost:8080" as absolute, reading
            // "localhost" as the scheme. Left unchecked, that value would sail through configuration
            // and only fail later as an unexplained transport error.
            if (!Uri.TryCreate(explicitUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new BillingConfigurationException(
                    $"'Maxio:BaseUrl' is set to '{explicitUrl}', which is not an absolute http or https URL.");
            }

            return explicitUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                "Neither 'Maxio:BaseUrl' nor 'Maxio:Subdomain' is configured, so there is no Maxio host to call.");
        }

        var template = IsEuRegion ? EuBaseUrlTemplate : UsBaseUrlTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }

    /// <summary>
    /// Verifies that everything the integration cannot run without is present. Called once when the
    /// client is constructed so misconfiguration surfaces as a clear error rather than an
    /// unauthorized call.
    /// </summary>
    /// <exception cref="BillingConfigurationException">A required setting is missing.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                "'Maxio:ApiKey' is not configured. Set it with 'dotnet user-secrets set \"Maxio:ApiKey\" \"<key>\"'.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException("'Maxio:ProductFamilyHandle' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(MeteredComponentHandle))
        {
            throw new BillingConfigurationException("'Maxio:MeteredComponentHandle' is not configured.");
        }

        // Throws when neither a base URL nor a subdomain is available.
        ResolveBaseUrl();
    }
}
