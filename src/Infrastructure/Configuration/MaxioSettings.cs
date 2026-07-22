using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section (mirroring how <see cref="CatalogSettings"/> is bound). The API key arrives through
/// .NET user-secrets or the environment and never appears in a committed file.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string ConfigurationSection = "Maxio";

    private const string UsProductionHostFormat = "https://{0}.chargify.com";
    private const string EuProductionHostFormat = "https://{0}.ebilling.maxio.com";
    private const string EuEnvironment = "EU";

    /// <summary>The Maxio API key, used as the Basic-auth username. Secret — never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The subdomain of the Maxio site, e.g. "apimatic-hackathon".</summary>
    public string? Subdomain { get; set; }

    /// <summary>The Maxio data-centre region ("US" or "EU") — not the deployment target.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Explicit outbound base URL. When set it wins over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant or a local mock server purely
    /// through configuration (plan §2.3).
    /// </summary>
    public string? BaseUrl { get; set; }

    public string? ProductFamilyHandle { get; set; }
    public int ProductFamilyId { get; set; }
    public string? DefaultProductHandle { get; set; }
    public int DefaultProductId { get; set; }
    public string? AlternateProductHandle { get; set; }
    public int AlternateProductId { get; set; }
    public string? MeteredComponentHandle { get; set; }
    public int MeteredComponentId { get; set; }

    /// <summary>How long a single outbound call may take before it is abandoned.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a transient failure is retried before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>The base delay for the exponential back-off between retries.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    /// <summary>
    /// The single place the outbound target is decided: an explicit <see cref="BaseUrl"/> is used
    /// verbatim, otherwise the host is derived from <see cref="Subdomain"/> and the region.
    /// </summary>
    public string ResolveBaseUrl() =>
        TryResolveBaseUrl(out var baseUrl)
            ? baseUrl
            : throw new BillingConfigurationException("No billing provider target is configured. Set 'Maxio:BaseUrl' to the target server, or 'Maxio:Subdomain' to derive it.");

    /// <summary>The non-throwing form, so startup validation can report the problem itself.</summary>
    public bool TryResolveBaseUrl(out string baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            baseUrl = EnsureTrailingSlash(BaseUrl.Trim());

            return Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            baseUrl = string.Empty;

            return false;
        }

        var format = string.Equals(Environment?.Trim(), EuEnvironment, StringComparison.OrdinalIgnoreCase)
            ? EuProductionHostFormat
            : UsProductionHostFormat;

        baseUrl = EnsureTrailingSlash(string.Format(format, Subdomain.Trim()));

        return true;
    }

    /// <summary>Projects the configured seed entities onto the provider-agnostic catalog.</summary>
    public BillingCatalog ToCatalog() => new()
    {
        ProductFamilyHandle = ProductFamilyHandle ?? string.Empty,
        DefaultPlanHandle = DefaultProductHandle ?? string.Empty,
        AlternatePlanHandle = AlternateProductHandle ?? string.Empty,
        MeteredComponentHandle = MeteredComponentHandle ?? string.Empty
    };

    // HttpClient only combines relative request paths against a base address that ends in a slash.
    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
