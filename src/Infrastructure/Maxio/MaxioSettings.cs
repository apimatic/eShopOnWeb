using System;
using System.Collections.Generic;
using System.Linq;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Everything the Maxio integration reads from configuration. Bound from the <c>Maxio</c> section; the
/// credential-bearing members are expected to arrive from user-secrets or the environment
/// (<c>Maxio__ApiKey</c>, …) and are never committed.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Sent as the Basic-auth user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain. Used to derive the API base address unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family the sellable plans live in.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set it is used verbatim as the API base address and the subdomain is
    /// ignored — useful for a mock server or a gateway in front of Maxio.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Plan handle used when a subscribe request does not name one. Left unset, a request without a
    /// plan handle is rejected rather than guessed at.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Maxio should collect payment for subscriptions this application creates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <c>remittance</c> — invoice the customer rather than charge a card at signup. Without
    /// it Maxio attempts to collect the first period immediately and rejects the enrolment with
    /// "No payment method was on file", even for a plan whose <c>require_credit_card</c> is false. This
    /// integration deliberately captures no card details, so remittance is the collection method that
    /// matches what it actually does.
    /// </para>
    /// <para>
    /// Maxio accepts <c>remittance</c>, <c>automatic</c> and <c>prepaid</c> on the Relationship
    /// Invoicing architecture, and <c>invoice</c> or <c>automatic</c> on the legacy Statements
    /// architecture — so a site on the older architecture sets this to <c>invoice</c>. Set it to empty
    /// to omit the field entirely and let Maxio apply the site default.
    /// </para>
    /// </remarks>
    public string? PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Namespace for the customer references this application owns, so several applications can share
    /// one Maxio site without colliding.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>Per-attempt HTTP timeout. Backstop below <see cref="RetryTimeoutSeconds"/>.</summary>
    public int HttpTimeoutSeconds { get; set; } = 15;

    /// <summary>Per-attempt timeout enforced by the SDK's retry pipeline.</summary>
    public int RetryTimeoutSeconds { get; set; } = 10;

    /// <summary>Extra attempts after the first. The SDK's floor is 1, so 1 still means two attempts.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Budget for a whole logical call including every retry and all backoff. This is the only bound
    /// the caller actually experiences, so it must sit below the deadline they are working to.
    /// </summary>
    public int CallBudgetSeconds { get; set; } = 30;

    /// <summary>
    /// How long the product-family handle-to-id resolution is cached. Maxio reassigns numeric ids when
    /// a site is re-seeded, so this cannot be cached for the process lifetime.
    /// </summary>
    public int CatalogCacheSeconds { get; set; } = 300;

    /// <summary>
    /// Logs the method, URI and status of every Maxio request at Debug level. Intended for verifying a
    /// new call on the wire — the SDK reports neither URL nor status on a successful response.
    /// </summary>
    public bool LogRequests { get; set; }

    /// <summary>True once there is enough configuration to attempt a call at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// Returns one message per configuration problem, naming the key at fault. Never includes a value:
    /// these messages reach logs.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{SectionName}:{nameof(ApiKey)}' is missing.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add($"'{SectionName}:{nameof(Subdomain)}' is missing (or set '{SectionName}:{nameof(BaseUrl)}' to override the API base address).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is missing.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl)
            && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            problems.Add($"'{SectionName}:{nameof(BaseUrl)}' is not an absolute URL.");
        }

        if (!string.IsNullOrWhiteSpace(PaymentCollectionMethod)
            && !CollectionMethod.FromValue(PaymentCollectionMethod).IsKnownValue())
        {
            problems.Add(
                $"'{SectionName}:{nameof(PaymentCollectionMethod)}' is not a collection method Maxio recognises. "
                + $"Expected one of: {string.Join(", ", CollectionMethod.GetKnownValues().Select(v => v.Value))}.");
        }

        if (HttpTimeoutSeconds <= 0) problems.Add($"'{SectionName}:{nameof(HttpTimeoutSeconds)}' must be greater than zero.");
        if (RetryTimeoutSeconds <= 0) problems.Add($"'{SectionName}:{nameof(RetryTimeoutSeconds)}' must be greater than zero.");
        if (CallBudgetSeconds <= 0) problems.Add($"'{SectionName}:{nameof(CallBudgetSeconds)}' must be greater than zero.");
        if (CatalogCacheSeconds < 0) problems.Add($"'{SectionName}:{nameof(CatalogCacheSeconds)}' cannot be negative.");

        // The SDK's retry pipeline validates its attempt count as >= 1 and throws at client
        // construction otherwise, so reject 0 here where the message can still name the key.
        if (MaxRetries < 1) problems.Add($"'{SectionName}:{nameof(MaxRetries)}' must be at least 1 (the SDK cannot disable retries).");

        return problems;
    }
}
