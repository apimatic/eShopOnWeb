using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. Values come from user-secrets in
/// development and from environment variables / a secret store elsewhere — never from a committed file.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Advanced Billing API key (HTTP Basic user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain the base URL is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used as the API base address instead of the subdomain-derived one.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary><c>Maxio:DefaultPlanHandle</c> — optional plan used when a subscribe request names none.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — how new subscriptions are collected. eShopOnWeb captures no
    /// payment method, so the default is <c>remittance</c> (the customer is invoiced); use <c>invoice</c>
    /// on a legacy Statements site, or <c>automatic</c> once a payment profile is on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary><c>Maxio:RequestTimeoutSeconds</c> — total budget for one provider call, retries included.</summary>
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary><c>Maxio:AttemptTimeoutSeconds</c> — budget for a single HTTP attempt.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary><c>Maxio:PlanCacheSeconds</c> — how long the plan catalog is cached; 0 disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    public bool IsEuEnvironment =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns one message per problem; empty when the settings are usable. Never includes a value.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add("Maxio:ApiKey is not configured.");
        }
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add("Maxio:ProductFamilyHandle is not configured.");
        }
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                problems.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            problems.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }
        if (!string.IsNullOrWhiteSpace(Environment)
            && !string.Equals(Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !IsEuEnvironment)
        {
            problems.Add("Maxio:Environment must be 'US' or 'EU'.");
        }
        if (!CollectionMethod.TryGetKnownValue(PaymentCollectionMethod?.Trim(), out _))
        {
            problems.Add("Maxio:PaymentCollectionMethod must be one of: automatic, remittance, prepaid, invoice.");
        }
        if (RequestTimeoutSeconds <= 0 || AttemptTimeoutSeconds <= 0)
        {
            problems.Add("Maxio:RequestTimeoutSeconds and Maxio:AttemptTimeoutSeconds must be positive.");
        }
        if (PlanCacheSeconds < 0)
        {
            problems.Add("Maxio:PlanCacheSeconds must not be negative.");
        }
        return problems;
    }
}
