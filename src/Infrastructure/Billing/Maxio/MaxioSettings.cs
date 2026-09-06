using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Binding target for the <c>Maxio:</c> configuration section.
/// <para>
/// Nothing here has a baked-in site or catalog value: the same build is expected to run against a
/// different Maxio site and a different product family purely by rebinding this section. Supply
/// the values through user-secrets, environment variables (<c>Maxio__ApiKey</c> and friends) or a
/// secret store — never through a file in the repository.
/// </para>
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key. Sent as the HTTP Basic user name, with the literal
    /// password "x", which is the authentication scheme the Advanced Billing API defines.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim, which is how you
    /// point the integration at the EU host (<c>https://{subdomain}.ebilling.maxio.com</c>) or at
    /// a recording proxy. When empty the US host is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Collection method used when enrolling a shopper. Left empty, the integration reads the
    /// site once and picks the method that lets a signup succeed without a payment method on
    /// file: "remittance" on Relationship Invoicing sites, "invoice" on legacy sites. Set it
    /// explicitly (for example to "automatic") to take that decision yourself.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a throttled or transient call is retried before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// How long the plan catalog is cached in memory. The catalog changes rarely and every
    /// subscribe call reads it, so a short cache removes most of the traffic.
    /// </summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>
    /// The problems that stop this configuration from being usable. Empty means usable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{SectionName}:ApiKey' is not set");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add($"neither '{SectionName}:Subdomain' nor '{SectionName}:BaseUrl' is set");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{SectionName}:ProductFamilyHandle' is not set");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            problems.Add($"'{SectionName}:BaseUrl' is not an absolute URI");
        }

        if (TimeoutSeconds <= 0)
        {
            problems.Add($"'{SectionName}:TimeoutSeconds' must be greater than zero");
        }

        if (MaxRetryAttempts < 0)
        {
            problems.Add($"'{SectionName}:MaxRetryAttempts' must not be negative");
        }

        return problems;
    }

    public bool IsConfigured => Validate().Count == 0;

    /// <summary>
    /// The base address for API calls: <see cref="BaseUrl"/> verbatim when supplied, otherwise the
    /// US Advanced Billing host derived from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var configured = BaseUrl!.Trim();

            // A base address without a trailing slash silently drops its last path segment when
            // combined with a relative URI, which would break a proxy mounted on a sub-path.
            if (!configured.EndsWith("/", StringComparison.Ordinal))
            {
                configured += "/";
            }

            return new Uri(configured, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Cannot resolve the Maxio base address: set '{SectionName}:Subdomain' or '{SectionName}:BaseUrl'.");
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/", UriKind.Absolute);
    }
}
