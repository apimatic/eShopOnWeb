using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Every value is supplied by configuration - nothing about a particular Maxio site or catalog
/// is compiled in, so the same build runs against a different site and a different product family.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    private static readonly HashSet<string> KnownCollectionMethods =
        new(StringComparer.Ordinal) { "automatic", "remittance", "invoice", "prepaid" };

    /// <summary>Maxio API key. Sent as the HTTP Basic user name. Load from user-secrets or the environment.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, substituted into the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim override of the API base address. When set it is used exactly as given and the
    /// subdomain is not substituted; when unset the base address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How Maxio should collect payment for subscriptions this application creates. eShopOnWeb captures no
    /// card and performs no 3-D Secure flow, so the default is invoice-style collection
    /// (<c>remittance</c>): a plan whose <c>require_credit_card</c> is false is still rejected under the
    /// default <c>automatic</c> collection, because Maxio tries to charge the first period immediately.
    /// </summary>
    /// <remarks>
    /// Which values are legal depends on the site's billing architecture: <c>remittance</c>,
    /// <c>automatic</c> and <c>prepaid</c> under Relationship Invoicing; <c>invoice</c> and
    /// <c>automatic</c> under the legacy Statements architecture. Set to an empty value to omit the field
    /// and let the site's own default apply.
    /// </remarks>
    public string? PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Optional days after renewal that an invoice-billed subscription is due (0-180). Sent only when set.
    /// </summary>
    public string? NetTerms { get; set; }

    /// <summary>Bound on a single HTTP attempt against Maxio.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Bound on a whole logical operation, retries and backoff included.</summary>
    public int RequestBudgetSeconds { get; set; } = 30;

    /// <summary>
    /// Extra attempts the SDK may make after the first. The SDK's retry pipeline rejects a value below 1,
    /// so 1 is the floor - retries cannot be switched off, which is why writes carry a send-once guard.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>How long a resolved product-family id is trusted before it is looked up again.</summary>
    public int ProductFamilyCacheSeconds { get; set; } = 300;

    /// <summary>Page size used when walking the provider's list endpoints.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Safety valve so a provider that ignores paging cannot spin the page loop forever.</summary>
    public int MaxPages { get; set; } = 50;

    /// <summary>Logs every outbound Maxio request and response status. Diagnostics only - off by default.</summary>
    public bool LogRequests { get; set; }

    /// <summary>True when enough configuration is present to attempt a call at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>True when a verbatim base address was supplied and should be used as-is.</summary>
    public bool HasExplicitBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>
    /// Returns the reasons this configuration cannot be used, or an empty list when it is complete.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            failures.Add($"'{SectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            failures.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && !HasExplicitBaseUrl)
        {
            failures.Add(
                $"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
        }

        if (HasExplicitBaseUrl && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add($"'{SectionName}:{nameof(BaseUrl)}' must be an absolute URL when set.");
        }

        if (AttemptTimeoutSeconds <= 0)
        {
            failures.Add($"'{SectionName}:{nameof(AttemptTimeoutSeconds)}' must be greater than zero.");
        }

        if (RequestBudgetSeconds <= 0)
        {
            failures.Add($"'{SectionName}:{nameof(RequestBudgetSeconds)}' must be greater than zero.");
        }

        if (MaxRetries < 1)
        {
            failures.Add($"'{SectionName}:{nameof(MaxRetries)}' must be at least 1.");
        }

        if (PageSize <= 0)
        {
            failures.Add($"'{SectionName}:{nameof(PageSize)}' must be greater than zero.");
        }

        if (MaxPages <= 0)
        {
            failures.Add($"'{SectionName}:{nameof(MaxPages)}' must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(PaymentCollectionMethod)
            && !KnownCollectionMethods.Contains(PaymentCollectionMethod.Trim().ToLowerInvariant()))
        {
            failures.Add(
                $"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of {string.Join(", ", KnownCollectionMethods)}, or empty to omit it.");
        }

        if (!string.IsNullOrWhiteSpace(NetTerms)
            && (!int.TryParse(NetTerms, NumberStyles.Integer, CultureInfo.InvariantCulture, out var netTerms)
                || netTerms < 0
                || netTerms > 180))
        {
            failures.Add($"'{SectionName}:{nameof(NetTerms)}' must be a whole number of days between 0 and 180.");
        }

        return failures;
    }
}
