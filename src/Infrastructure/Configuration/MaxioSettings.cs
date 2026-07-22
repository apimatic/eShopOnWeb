using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Only <see cref="ApiKey"/> is sensitive and it arrives through user-secrets or the
/// environment - never through a file in this repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_SECTION = "Maxio";

    /// <summary>The Maxio data-centre region hosting the site: US (default) or EU.</summary>
    public const string EuropeanRegion = "EU";

    /// <summary>The Maxio API key, used as the username of the HTTP Basic credentials.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The site subdomain, e.g. apimatic-hackathon. Used to derive the host when no BaseUrl is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The Maxio data-centre region (US/EU). This is a separate axis from the deployment target.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Explicit outbound base URL. When set it wins over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant, or a local mock server through
    /// configuration alone.
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

    /// <summary>How long a single outbound attempt may take before it is abandoned.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>The ceiling on one logical call including every retry, so no request hangs unbounded.</summary>
    public int OverallTimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a transient failure (timeout, 5xx, 429) is retried.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>The delay before the first retry; subsequent retries back off exponentially.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    /// <summary>
    /// The single place the outbound target is decided: an explicit <see cref="BaseUrl"/> is used
    /// verbatim, otherwise the host is derived from <see cref="Subdomain"/> and the region.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return EnsureTrailingSlash(BaseUrl.Trim());
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"Neither '{CONFIG_SECTION}:BaseUrl' nor '{CONFIG_SECTION}:Subdomain' is configured; the billing target server is unknown.");
        }

        var subdomain = Subdomain.Trim();
        var host = string.Equals(Environment?.Trim(), EuropeanRegion, StringComparison.OrdinalIgnoreCase)
            ? $"https://{subdomain}.ebilling.maxio.com"
            : $"https://{subdomain}.chargify.com";

        return EnsureTrailingSlash(host);
    }

    /// <summary>
    /// Fails fast at boot when the integration cannot possibly work: no credentials, no target
    /// server, or no seeded entities to operate on.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                $"'{CONFIG_SECTION}:ApiKey' is not configured. Set it with 'dotnet user-secrets set \"{CONFIG_SECTION}:ApiKey\" <key>' - it must never be committed.");
        }

        // Throws when neither an explicit target nor a subdomain to derive one from is present.
        ResolveBaseUrl();

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException($"'{CONFIG_SECTION}:ProductFamilyHandle' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(DefaultProductHandle))
        {
            throw new BillingConfigurationException($"'{CONFIG_SECTION}:DefaultProductHandle' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(MeteredComponentHandle))
        {
            throw new BillingConfigurationException($"'{CONFIG_SECTION}:MeteredComponentHandle' is not configured.");
        }
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
