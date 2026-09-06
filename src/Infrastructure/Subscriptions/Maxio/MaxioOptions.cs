using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Binds the <c>Maxio</c> configuration section. Nothing here has a hard-coded site or catalog value:
/// the same build has to run against any Maxio site.
/// </summary>
public class MaxioOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Advanced Billing API key. Sent as the HTTP Basic username (the password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain, e.g. the "acme" in <c>acme.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead of the address
    /// derived from <see cref="Subdomain"/> and <see cref="Environment"/> (useful for a proxy or a test double).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Advanced Billing hosting region: <c>US</c> (default) or <c>EU</c>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Plan used when a subscribe request does not name one. Optional: when it is not set and the
    /// product family offers exactly one plan, that plan is the default.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Overrides how Maxio collects payment for new subscriptions (<c>remittance</c>, <c>invoice</c>,
    /// <c>automatic</c> or <c>prepaid</c>). When unset it is derived from the site: relationship-invoicing
    /// sites get <c>remittance</c>, legacy statement sites get <c>invoice</c>. Both bill without a stored
    /// payment method, which is what lets a shopper subscribe without card capture.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Namespace applied to the customer/subscription <c>reference</c> values written into Maxio, so a
    /// shared Maxio site can host more than one application.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>How long the plan catalog and site metadata are cached before being re-read from Maxio.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Per-request timeout for calls to Maxio.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a safe (read-only) call is retried before giving up.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>True when enough is configured to talk to Maxio at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>Returns one message per configuration problem; empty when the options are usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"{SectionName}:{nameof(ApiKey)} is required.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            errors.Add($"{SectionName}:{nameof(Subdomain)} is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"{SectionName}:{nameof(ProductFamilyHandle)} is required.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add($"{SectionName}:{nameof(BaseUrl)} must be an absolute URL when set.");
        }

        if (!string.IsNullOrWhiteSpace(Environment) &&
            !string.Equals(Environment, "US", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{SectionName}:{nameof(Environment)} must be 'US' or 'EU'.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:{nameof(Timeout)} must be positive.");
        }

        if (RetryCount < 0)
        {
            errors.Add($"{SectionName}:{nameof(RetryCount)} cannot be negative.");
        }

        return errors;
    }
}
