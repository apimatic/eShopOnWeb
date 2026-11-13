using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio Advanced Billing settings, bound from the <c>Maxio</c> configuration section.
/// Values come from user-secrets in development and from the host's configuration in production;
/// none of them has a value in source control.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain the API base address is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// <c>Maxio:BaseUrl</c> — optional. When set it is used verbatim as the API base address instead of
    /// one derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — how new subscriptions are collected. Defaults to <c>remittance</c>:
    /// this API captures no payment method, and Maxio refuses an <c>automatic</c> signup with a balance due and
    /// no payment method on file. Use <c>invoice</c> on a legacy Statements-architecture site.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Per-attempt timeout for one HTTP attempt to Maxio.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Total budget for all Maxio calls made while serving one API request.</summary>
    public int RequestBudgetSeconds { get; set; } = 30;

    /// <summary>Page size used when listing the family's products (Maxio caps it at 200).</summary>
    public int PlansPerPage { get; set; } = 100;

    /// <summary>Page cap when listing plans; reaching it marks the result as truncated.</summary>
    public int MaxPlanPages { get; set; } = 10;

    /// <summary>How long the plan list served by <c>GET /api/subscription-plans</c> is cached.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>How long a claim may stay pending before another request may settle and take it over.</summary>
    public int PendingClaimTimeoutSeconds { get; set; } = 120;

    public bool IsEu => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Never prints the API key.</summary>
    public override string ToString() =>
        $"Maxio {{ Subdomain = {Subdomain}, BaseUrl = {BaseUrl}, ProductFamilyHandle = {ProductFamilyHandle}, Environment = {Environment ?? "US"}, ApiKey = {(string.IsNullOrWhiteSpace(ApiKey) ? "<missing>" : "<set>")} }}";
}

/// <summary>
/// Refuses to start the host when Maxio is not configured, instead of failing the first shopper's request.
/// Messages name the missing key and never echo a value.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets or the host's configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when set.");
        }

        if (!string.IsNullOrWhiteSpace(options.Environment) &&
            !string.Equals(options.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase) &&
            !options.IsEu)
        {
            failures.Add("Maxio:Environment must be US or EU when set.");
        }

        if (options.PaymentCollectionMethod is not ("automatic" or "remittance" or "prepaid" or "invoice"))
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of automatic, remittance, prepaid, invoice.");
        }

        if (options.AttemptTimeoutSeconds <= 0 || options.RequestBudgetSeconds <= 0 ||
            options.PlansPerPage is <= 0 or > 200 || options.MaxPlanPages <= 0 ||
            options.PlanCacheSeconds < 0 || options.PendingClaimTimeoutSeconds <= options.RequestBudgetSeconds)
        {
            failures.Add("Maxio timing/paging settings are out of range (PlansPerPage 1-200; PendingClaimTimeoutSeconds must exceed RequestBudgetSeconds).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
