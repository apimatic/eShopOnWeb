using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for talking to Maxio Advanced Billing, bound from the "Maxio" configuration
/// section. Values come from user-secrets / environment configuration and are never
/// committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the user name of HTTP basic auth, with the literal password
    /// "x", which is the scheme documented for the Advanced Billing API.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Maxio site, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set it is used verbatim as the API base address, instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-request timeout. Defaults to 30 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a retryable call is re-sent before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    /// <summary>
    /// The address every request is sent to. Ends with a slash so relative request paths
    /// resolve against the full configured address rather than replacing its last segment.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var address = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain?.Trim()}.chargify.com"
            : BaseUrl!.Trim();

        if (!address.EndsWith("/", StringComparison.Ordinal))
        {
            address += "/";
        }

        return new Uri(address, UriKind.Absolute);
    }
}
