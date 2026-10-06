using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Secrets come from user-secrets / environment
/// (e.g. <c>Maxio__ApiKey</c>) — never from a checked-in file.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (sent as the Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; the API host is derived from it unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional: used verbatim as the API base address instead of deriving one from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Hosting region of the Maxio site: <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Total seconds one API request may wait on Maxio (before the settle step).</summary>
    public int RequestBudgetSeconds { get; set; } = 25;

    /// <summary>Seconds allowed to settle an unanswered subscription write by looking it up.</summary>
    public int SettleBudgetSeconds { get; set; } = 4;

    /// <summary>Per-attempt timeout of a single HTTP call to Maxio.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Retries of idempotent reads (GET); writes are never resent.</summary>
    public int MaxReadRetries { get; set; } = 2;

    /// <summary>Upper bound on plan-list pages (200 plans each) read per request.</summary>
    public int MaxPlanPages { get; set; } = 5;

    /// <summary>Seconds the plan list is cached.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Maxio payment collection method for new subscriptions. This integration captures no card, so the
    /// default is <c>remittance</c> (invoice-based; Relationship Invoicing sites). Legacy Statements sites use
    /// <c>invoice</c>; <c>automatic</c> only works where a payment method is already on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public CollectionMethod CollectionMethod =>
        CollectionMethod.TryGetKnownValue(PaymentCollectionMethod?.Trim().ToLowerInvariant(), out var known)
            ? known
            : CollectionMethod.Remittance;

    /// <summary>Prefix of customer/subscription references sent to Maxio; keeps deployments sharing a site apart.</summary>
    public string ReferencePrefix { get; set; } = "eshop";

    /// <summary>The longest any caller may be kept waiting, in seconds (task requirement).</summary>
    public const int MaxCallerWaitSeconds = 30;

    public bool IsEu => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the configuration problems; empty when the settings are usable. Never echoes values.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add($"{SectionName}:ApiKey is not configured. Set it via user-secrets or the environment.");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            errors.Add($"{SectionName}:Subdomain is not configured (required unless {SectionName}:BaseUrl is set).");
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            errors.Add($"{SectionName}:BaseUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            errors.Add($"{SectionName}:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(Environment)
            && !string.Equals(Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase) && !IsEu)
            errors.Add($"{SectionName}:Environment must be 'US' or 'EU'.");
        if (RequestBudgetSeconds <= 0 || SettleBudgetSeconds <= 0 || AttemptTimeoutSeconds <= 0)
            errors.Add($"{SectionName} timeouts must be positive.");
        if (RequestBudgetSeconds + SettleBudgetSeconds > MaxCallerWaitSeconds)
            errors.Add($"{SectionName}:RequestBudgetSeconds + SettleBudgetSeconds must not exceed {MaxCallerWaitSeconds}.");
        if (MaxReadRetries < 0 || MaxPlanPages < 1 || PlanCacheSeconds < 0)
            errors.Add($"{SectionName}:MaxReadRetries/MaxPlanPages/PlanCacheSeconds are out of range.");
        if (!CollectionMethod.TryGetKnownValue(PaymentCollectionMethod?.Trim().ToLowerInvariant(), out _))
            errors.Add($"{SectionName}:PaymentCollectionMethod must be one of: remittance, invoice, automatic, prepaid.");
        if (string.IsNullOrWhiteSpace(ReferencePrefix))
            errors.Add($"{SectionName}:ReferencePrefix must not be blank.");
        return errors;
    }
}
