using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the
/// <see cref="ConfigurationSection"/> section. Only <see cref="ApiKey"/> is sensitive and it must arrive
/// through .NET user-secrets or the environment — never from a file in source control.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section these settings bind from.</summary>
    public const string ConfigurationSection = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>Maxio API key. Supplied via user-secrets / environment only.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the host when no explicit base URL is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Maxio data-centre region — <c>US</c> (default) or <c>EU</c>. This is the provider's hosting region
    /// and is a separate axis from the deployment target controlled by <see cref="BaseUrl"/>.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Explicit outbound base URL. When set it wins verbatim over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant, or a local mock purely through
    /// configuration. Leave empty to derive the host from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional pre-resolved product family id. Provider ids are reassigned when the catalog is re-seeded,
    /// so when this is absent the id is resolved from <see cref="ProductFamilyHandle"/> at runtime.
    /// </summary>
    public int? ProductFamilyId { get; set; }

    public string? DefaultProductHandle { get; set; }

    public int? DefaultProductId { get; set; }

    public string? AlternateProductHandle { get; set; }

    public int? AlternateProductId { get; set; }

    public string? MeteredComponentHandle { get; set; }

    public int? MeteredComponentId { get; set; }

    /// <summary>
    /// Bound on a single outbound billing call, retries included. Keeps a slow provider from holding a
    /// request thread indefinitely.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>True when the configured region is Maxio's EU data centre.</summary>
    public bool IsEuropeanRegion =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verifies everything the integration needs in order to run is present, so a misconfigured deployment
    /// is rejected at startup rather than on a customer's first request.
    /// </summary>
    /// <exception cref="BillingConfigurationException">Required configuration is missing or invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                $"'{ConfigurationSection}:ApiKey' is not configured. Supply it through user-secrets or the environment.");
        }

        if (RequestTimeoutSeconds <= 0)
        {
            throw new BillingConfigurationException(
                $"'{ConfigurationSection}:RequestTimeoutSeconds' must be greater than zero.");
        }

        // Throws when neither an explicit base URL nor a subdomain can produce a valid host.
        ResolveBaseUrl();
    }

    /// <summary>
    /// Resolves the outbound base URL: an explicit <see cref="BaseUrl"/> is used verbatim, otherwise the
    /// host is derived from <see cref="Subdomain"/> and the region. This is the single place retargeting
    /// happens, so pointing at another environment is a configuration change and never a code change.
    /// </summary>
    /// <exception cref="BillingConfigurationException">
    /// Neither an explicit base URL nor a subdomain was configured, or the resulting value is not a valid
    /// absolute URL.
    /// </exception>
    public string ResolveBaseUrl()
    {
        var resolved = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl.Trim()
            : DeriveHostFromSubdomain();

        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BillingConfigurationException(
                $"The configured billing base URL '{resolved}' is not a valid absolute http(s) URL.");
        }

        return resolved;
    }

    private string DeriveHostFromSubdomain()
    {
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"Neither '{ConfigurationSection}:BaseUrl' nor '{ConfigurationSection}:Subdomain' is configured, so the billing host cannot be determined.");
        }

        var template = IsEuropeanRegion ? EuHostTemplate : UsHostTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
