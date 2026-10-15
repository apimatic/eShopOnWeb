using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing settings, bound from the <c>Maxio:</c> configuration section.
/// Values come from configuration only (user-secrets, environment variables, a secret store) — never from source.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (sent as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain that fills the base URL template.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim instead of the subdomain-derived address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — how new subscriptions are collected. Defaults to <c>remittance</c>
    /// (invoice-based, no card on file — this integration does not capture payment details). Use <c>invoice</c> on a
    /// legacy Statements-architecture site, or <c>automatic</c> once a payment profile flow exists.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary><c>Maxio:AttemptTimeoutSeconds</c> — bound on one HTTP attempt.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary><c>Maxio:CallBudgetSeconds</c> — bound on one SDK call including its retries.</summary>
    public int CallBudgetSeconds { get; set; } = 15;

    /// <summary><c>Maxio:PlanCacheSeconds</c> — how long the plan catalog is cached.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    public TimeSpan AttemptTimeout => TimeSpan.FromSeconds(AttemptTimeoutSeconds);

    public TimeSpan CallBudget => TimeSpan.FromSeconds(CallBudgetSeconds);

    /// <summary>Transport backstop: a little longer than one attempt so the SDK's own per-attempt timeout fires first.</summary>
    public TimeSpan HttpClientTimeout => TimeSpan.FromSeconds(AttemptTimeoutSeconds + 5);

    public TimeSpan PlanCacheDuration => TimeSpan.FromSeconds(PlanCacheSeconds);

    internal static void Bind(IConfiguration section, MaxioSettings settings)
    {
        settings.ApiKey = section[nameof(ApiKey)];
        settings.Subdomain = section[nameof(Subdomain)];
        settings.ProductFamilyHandle = section[nameof(ProductFamilyHandle)];
        settings.BaseUrl = section[nameof(BaseUrl)];
        settings.Environment = section[nameof(Environment)] is { Length: > 0 } environment ? environment.Trim().ToUpperInvariant() : settings.Environment;
        settings.PaymentCollectionMethod = section[nameof(PaymentCollectionMethod)] is { Length: > 0 } collection
            ? collection.Trim().ToLowerInvariant()
            : settings.PaymentCollectionMethod;
        settings.AttemptTimeoutSeconds = ReadInt(section, nameof(AttemptTimeoutSeconds), settings.AttemptTimeoutSeconds);
        settings.CallBudgetSeconds = ReadInt(section, nameof(CallBudgetSeconds), settings.CallBudgetSeconds);
        settings.PlanCacheSeconds = ReadInt(section, nameof(PlanCacheSeconds), settings.PlanCacheSeconds);
    }

    // An unparsable value becomes -1 so validation rejects it instead of silently using the default.
    private static int ReadInt(IConfiguration section, string key, int defaultValue) =>
        section[key] is not { Length: > 0 } raw ? defaultValue : int.TryParse(raw, out var value) ? value : -1;
}
