using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. No value has a default that points at a real
/// site or catalog: the same build runs against any Maxio site.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> - the Maxio API key (HTTP Basic username). Secret: user-secrets / environment only.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> - the Maxio site subdomain. Required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> - handle of the product family whose products are the offered plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> - optional. When set it is used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> - optional data-center region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> - optional, default <c>remittance</c>. The subscribe flow captures no
    /// payment method, so subscriptions are collected by invoice: <c>remittance</c> on Relationship Invoicing sites,
    /// <c>invoice</c> on legacy Statements sites. <c>automatic</c> needs a payment method on file and Maxio refuses a
    /// paid signup without one.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    public string EffectivePaymentCollectionMethod =>
        string.IsNullOrWhiteSpace(PaymentCollectionMethod) ? "remittance" : PaymentCollectionMethod.Trim().ToLowerInvariant();

    /// <summary>Total time one API request may spend talking to Maxio, all calls and retries included.</summary>
    public int RequestBudgetSeconds { get; set; } = 30;

    /// <summary>Per-attempt timeout of a single Maxio HTTP call.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Retries for idempotent reads (writes are never re-sent by the SDK).</summary>
    public int MaxReadRetries { get; set; } = 2;

    /// <summary>How long the plan list is cached.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>Upper bound on plan-list pages walked (200 plans per page).</summary>
    public int PlanPageCap { get; set; } = 5;

    /// <summary>
    /// After a write whose outcome is unknown, how long Maxio may still complete it (it keeps working on a request
    /// for up to 120 s after the client gave up). Until this has passed, "not found" does not prove "not created".
    /// </summary>
    public int UnknownOutcomeWindowSeconds { get; set; } = 150;

    public bool IsEu => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    public bool HasBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>
/// Startup validation (registered with <c>ValidateOnStart</c>): the host refuses to start rather than failing the
/// first shopper's request. Messages name the missing key and never echo a value.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured (set it via user-secrets or the MAXIO_API_KEY / Maxio__ApiKey environment variable).");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured (MAXIO_DEFAULT_PRODUCT_FAMILY / Maxio__ProductFamilyHandle).");
        }

        if (options.HasBaseUrl)
        {
            if (!Uri.TryCreate(options.BaseUrl!.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                failures.Add("Maxio:BaseUrl is set but is not an absolute http(s) URL.");
            }
        }
        else if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured (MAXIO_SITE_SUBDOMAIN / Maxio__Subdomain), and no Maxio:BaseUrl override is set.");
        }

        if (!string.IsNullOrWhiteSpace(options.Environment) && !options.IsEu &&
            !string.Equals(options.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be US or EU when set.");
        }

        if (!MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(options.EffectivePaymentCollectionMethod, out _))
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of remittance, invoice, automatic, prepaid.");
        }

        if (options.RequestBudgetSeconds <= 0 || options.AttemptTimeoutSeconds <= 0 || options.MaxReadRetries < 0 ||
            options.PlanCacheSeconds < 0 || options.PlanPageCap <= 0 || options.UnknownOutcomeWindowSeconds <= 0)
        {
            failures.Add("Maxio timing settings must be positive numbers.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
