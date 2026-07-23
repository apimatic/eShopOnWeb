using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration (mirrors how <c>CatalogSettings</c> is
/// bound). Only <see cref="ApiKey"/> is sensitive and it arrives through .NET user-secrets; the
/// handles, ids and <see cref="BaseUrl"/> are environment metadata and may equally come from
/// appsettings, an environment variable (<c>Maxio__BaseUrl</c>) or a launch profile.
/// </summary>
public class MaxioSettings
{
    /// <summary>Maxio's US data-center host template.</summary>
    private const string UsHostTemplate = "https://{0}.chargify.com";

    /// <summary>Maxio's EU data-center host template.</summary>
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Never committed — supplied through user-secrets.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "cp-exp-3".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-center region, "US" or "EU". This is a different axis from the deployment
    /// target: it selects which of Maxio's regions hosts the site, not prod/dev/mock.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base URL. When set it WINS over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant, or a local mock server purely
    /// through configuration (§2.3). Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How the provider collects payment for new subscriptions. The demo plans are seeded so that a
    /// payment method is required for automatic collection, so the default is "remittance"
    /// (invoice billing) — that is what lets UC1 enroll without card capture or 3-DS.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    /// <summary>The metered component usage is billed against, e.g. "api-call" (UC2).</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    /// <summary>
    /// Resolves the outbound target server: an explicit <see cref="BaseUrl"/> is used verbatim,
    /// otherwise the host is derived from <see cref="Subdomain"/> and the region
    /// <see cref="Environment"/>. This is the single place retargeting happens, so pointing the
    /// integration at prod, a dev tenant or a mock is a configuration change, never a recompile.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return EnsureTrailingSlash(BaseUrl.Trim());
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        var template = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? EuHostTemplate
            : UsHostTemplate;

        return EnsureTrailingSlash(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim()));
    }

    /// <summary>
    /// Keeps the trailing slash so a target that carries a path prefix (a mock served under
    /// <c>/api</c>, say) keeps that prefix when relative request URIs are combined with it.
    /// </summary>
    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
