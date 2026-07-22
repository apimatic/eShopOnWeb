using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section (plan.md §5). Only <see cref="ApiKey"/> is sensitive and it must come from user-secrets
/// or the environment — never from a committed file.
/// </summary>
public class MaxioSettings : ISubscriptionSettings
{
    /// <summary>
    /// The configuration section these settings bind from.
    /// </summary>
    public const string CONFIG_SECTION = "Maxio";

    private const string UsHostFormat = "https://{0}.chargify.com";
    private const string EuHostFormat = "https://{0}.ebilling.maxio.com";

    /// <summary>
    /// Maxio API key, used as the HTTP Basic username (the password is the literal "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Advanced Billing site subdomain, used to derive the host when no explicit
    /// <see cref="BaseUrl"/> is configured.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region — "US" or "EU". This is a separate axis from the deployment
    /// target (prod/dev/mock), which is controlled by <see cref="BaseUrl"/> (plan.md §2.3).
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base URL. When set it wins over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant, or a local mock server purely
    /// through configuration (plan.md §2.3). Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How the provider collects payment for new subscriptions. The demo plans are seeded with
    /// "requires payment method" off (plan.md UC0), so the default is invoice-style remittance:
    /// with the default "automatic" the provider refuses to enrol anyone without a card on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    /// <summary>
    /// Resolves the outbound base URL: an explicit <see cref="BaseUrl"/> is honoured verbatim,
    /// otherwise the host is derived from <see cref="Subdomain"/> and the region
    /// <see cref="Environment"/>. This is the single place retargeting happens (plan.md §2.3/§4.3).
    /// </summary>
    /// <remarks>
    /// A trailing slash is appended when missing so that relative request paths resolve against
    /// the configured URL rather than replacing its last segment. The host and path are otherwise
    /// left exactly as configured.
    /// </remarks>
    public string ResolveBaseUrl()
    {
        var target = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : DeriveHostFromSubdomain();

        return target.EndsWith('/') ? target : target + "/";
    }

    private string DeriveHostFromSubdomain()
    {
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain must be configured when no explicit Maxio:BaseUrl is supplied.");
        }

        var format = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? EuHostFormat
            : UsHostFormat;

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Subdomain.Trim());
    }
}
