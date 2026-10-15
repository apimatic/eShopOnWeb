using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. No value has a code default that
/// points at a real site or catalog: the site, key and product family always come from configuration.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — Advanced Billing API key (sent as the Basic-auth user name). Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site's subdomain; required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — <c>US</c> (default) or <c>EU</c> hosting.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — how subscriptions are collected. This checkout captures no
    /// payment method, so the default is <c>remittance</c> (invoiced; Relationship Invoicing sites). Use
    /// <c>invoice</c> on legacy Statements sites, or <c>automatic</c> only once a payment method is captured.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Per-attempt timeout for one HTTP attempt.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 8;

    /// <summary>Retries for idempotent reads (writes are never resent).</summary>
    public int MaxReadRetries { get; set; } = 2;

    /// <summary>Total budget for one billing operation (all calls it makes), linked to the caller's cancellation.</summary>
    public int OperationBudgetSeconds { get; set; } = 45;

    /// <summary>Budget for re-reading the billing system after a write whose outcome is unknown.</summary>
    public int SettlementBudgetSeconds { get; set; } = 15;

    /// <summary>How long the plan catalog is cached.</summary>
    public int PlanCacheSeconds { get; set; } = 300;

    /// <summary>Page cap for the plan listing (200 plans per page).</summary>
    public int MaxPlanPages { get; set; } = 5;

    /// <summary>How long an in-flight claim blocks a duplicate request before it is considered stale.</summary>
    public int ClaimLeaseSeconds { get; set; } = 120;

    public TimeSpan AttemptTimeout => TimeSpan.FromSeconds(AttemptTimeoutSeconds);
    public TimeSpan OperationBudget => TimeSpan.FromSeconds(OperationBudgetSeconds);
    public TimeSpan SettlementBudget => TimeSpan.FromSeconds(SettlementBudgetSeconds);
    public TimeSpan PlanCacheDuration => TimeSpan.FromSeconds(PlanCacheSeconds);
    public TimeSpan ClaimLease => TimeSpan.FromSeconds(ClaimLeaseSeconds);
}
