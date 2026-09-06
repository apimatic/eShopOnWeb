using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" section.
/// </summary>
/// <remarks>
/// Nothing here has a value baked into the repository. In development the values come from
/// .NET user-secrets (Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle); in a deployed
/// environment they come from environment variables or a secret store.
/// </remarks>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>The site API key. Sent as the HTTP Basic user name, with "X" as the password.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the API base address.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family holding the plans eShopOnWeb offers.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is required.")]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim; otherwise the
    /// address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional override for how Maxio collects payment on subscriptions this app creates
    /// ("automatic", "remittance", "invoice", "prepaid" - the valid set depends on the site).
    /// Left unset, the integration bills by invoice, because the subscribe flow captures no card.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>How long the billing site's own settings are cached before being re-read.</summary>
    [Range(0, 1440, ErrorMessage = "Maxio:SiteCacheMinutes must be between 0 and 1440.")]
    public int SiteCacheMinutes { get; set; } = 15;

    /// <summary>How long a single call to Maxio may take before it is abandoned.</summary>
    [Range(1, 300, ErrorMessage = "Maxio:TimeoutSeconds must be between 1 and 300.")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a throttled or failed call is retried before giving up.</summary>
    [Range(0, 10, ErrorMessage = "Maxio:MaxRetries must be between 0 and 10.")]
    public int MaxRetries { get; set; } = 3;

    /// <summary>The delay before the first retry; subsequent retries back off exponentially.</summary>
    [Range(0, 30000, ErrorMessage = "Maxio:RetryBaseDelayMilliseconds must be between 0 and 30000.")]
    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Resolves the API base address, preferring <see cref="BaseUrl"/> when it is configured.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var address = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain.Trim()}.chargify.com/"
            : BaseUrl.Trim();

        // HttpClient drops the last path segment of a base address that does not end in a slash.
        if (!address.EndsWith("/", StringComparison.Ordinal))
        {
            address += "/";
        }

        return new Uri(address, UriKind.Absolute);
    }
}
