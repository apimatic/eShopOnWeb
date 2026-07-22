using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio integration, bound from the <c>Maxio</c> section
/// (plan.md §5). Only <see cref="ApiKey"/> is sensitive and it arrives through user-secrets —
/// nothing here is committed to source control.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    private const string UsRegion = "US";
    private const string EuRegion = "EU";

    /// <summary>The Maxio API key. Supplied via user-secrets or the environment; never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the host when no explicit override is set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region — <c>US</c> or <c>EU</c>. This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = UsRegion;

    /// <summary>
    /// Explicit outbound base URL. When set it wins over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant, or a local mock purely through
    /// configuration (plan.md §2.3). Leave empty to derive the host from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    /// <summary>
    /// How Maxio collects payment for new subscriptions — one of <c>automatic</c>, <c>remittance</c>,
    /// <c>prepaid</c> or <c>invoice</c>. The demo plans require no payment method, so the default is
    /// invoice billing (<c>remittance</c>): the balance is invoiced rather than charged to a card,
    /// which is what lets UC1 enrol without card capture or 3-DS (plan.md §1.3).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// The one place the outbound target is decided: an explicit <see cref="BaseUrl"/> is honoured
    /// verbatim, otherwise the host is derived from <see cref="Subdomain"/> and the region. A
    /// trailing slash is ensured so relative request paths resolve against the full base path.
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
                $"Maxio is not configured: set either '{CONFIG_NAME}:BaseUrl' or '{CONFIG_NAME}:Subdomain'.");
        }

        var host = IsEuRegion()
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";

        return EnsureTrailingSlash(host);
    }

    private bool IsEuRegion() => string.Equals(Environment, EuRegion, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
