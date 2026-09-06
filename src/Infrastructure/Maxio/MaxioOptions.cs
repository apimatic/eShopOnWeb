using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Nothing here has a hard-coded site, catalog or credential value: the same build runs
/// against any Maxio site by changing configuration alone.
/// </summary>
/// <remarks>
/// Supply <see cref="ApiKey"/>, <see cref="Subdomain"/> and <see cref="ProductFamilyHandle"/> via
/// user-secrets or environment variables (Maxio__ApiKey, Maxio__Subdomain,
/// Maxio__ProductFamilyHandle). They must never be committed.
/// </remarks>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Advanced Billing API key. Sent as the HTTP Basic username with the literal password "x",
    /// per the spec's <c>BasicAuth</c> security scheme.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Subdomain of the Advanced Billing site. Substituted into the spec's server template
    /// <c>https://{site}.chargify.com</c> unless <see cref="BaseUrl"/> overrides it.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscribable plans. Only plans in this family
    /// are listed or subscribable.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute base address override, used verbatim when set. Required for sites hosted
    /// outside the spec's default US server (for example EU sites on
    /// <c>https://{site}.ebilling.maxio.com</c>).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional plan handle used when a subscribe request omits one. Left unset, omitting the plan
    /// handle is a client error rather than a silent choice of plan.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Collection method applied to subscriptions this integration creates. One of the spec's
    /// Collection-Method values: automatic, remittance, prepaid, invoice.
    /// </summary>
    /// <remarks>
    /// Defaults to "remittance" (invoice the customer) because eShopOnWeb captures no card at
    /// signup. With "automatic", Advanced Billing tries to collect the first period immediately
    /// and rejects the signup with "No payment method was on file" — even for plans whose
    /// require_credit_card is false. Set to "automatic" on sites where a payment profile is
    /// attached before subscribing.
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Prefix for the customer reference this app stores in Maxio, namespacing eShopOnWeb
    /// customers on a shared site.</summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>How long the plan catalog and site metadata are cached. Zero disables caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>Total budget for one Maxio call, retries included.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Retries for transient failures (429, 5xx, connection faults). Zero disables retrying.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    /// <summary>Page size used when walking Maxio's paged list endpoints. Capped at the spec maximum of 200.</summary>
    public int PageSize { get; set; } = 200;

    /// <summary>
    /// Collection methods the Maxio spec accepts (components/schemas/Collection-Method.yaml).
    /// </summary>
    internal static readonly string[] SupportedCollectionMethods = { "automatic", "remittance", "prepaid", "invoice" };

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when set, otherwise the spec's
    /// server template with <see cref="Subdomain"/> substituted for <c>{site}</c>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var raw = BaseUrl.Trim();
            return new Uri(raw.EndsWith('/') ? raw : raw + "/", UriKind.Absolute);
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/", UriKind.Absolute);
    }

    /// <summary>
    /// Returns every configuration problem that would stop the integration from working, or an
    /// empty list when the options are usable.
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

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            failures.Add($"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            failures.Add($"'{SectionName}:{nameof(BaseUrl)}' must be an absolute URI.");
        }

        if (!SupportedCollectionMethods.Contains(PaymentCollectionMethod, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add(
                $"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of: " +
                string.Join(", ", SupportedCollectionMethods) + ".");
        }

        if (TimeoutSeconds <= 0)
        {
            failures.Add($"'{SectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }

        if (MaxRetryAttempts < 0)
        {
            failures.Add($"'{SectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        if (PageSize is < 1 or > 200)
        {
            failures.Add($"'{SectionName}:{nameof(PageSize)}' must be between 1 and 200.");
        }

        return failures;
    }
}
