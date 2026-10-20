using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. Credentials are never stored in the repository:
/// supply them through user-secrets in development and the host's secret store elsewhere.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Maxio API key (secret).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain; required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional Maxio region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Upper bound for a single HTTP attempt to Maxio.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Upper bound for everything one API request does against Maxio (all calls and retries).</summary>
    public int CallBudgetSeconds { get; set; } = 30;

    /// <summary>Upper bound for re-reading Maxio to settle a write whose outcome is unknown.</summary>
    public int ReconcileBudgetSeconds { get; set; } = 10;

    /// <summary>
    /// How long a claimed or unsettled write is left alone before another request may take it over.
    /// Must exceed <see cref="CallBudgetSeconds"/> so a request still working on a claim is never overtaken.
    /// </summary>
    public int ClaimLeaseSeconds { get; set; } = 90;

    /// <summary>Most pages of products read when listing plans (200 products per page).</summary>
    public int MaxPlanPages { get; set; } = 5;

    public bool UsesEuEnvironment => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Refuses to start the host when a Maxio setting is missing or malformed. Messages name the key, never its value.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();
        const string section = MaxioOptions.SectionName;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{section}:ApiKey is not configured. Set it via user-secrets or the host's secret store.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{section}:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add($"{section}:Subdomain is not configured (required unless {section}:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{section}:BaseUrl must be an absolute http(s) URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.Environment) && !options.UsesEuEnvironment &&
            !string.Equals(options.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{section}:Environment must be US or EU.");
        }

        if (options.AttemptTimeoutSeconds <= 0 || options.CallBudgetSeconds <= 0 ||
            options.ReconcileBudgetSeconds <= 0 || options.MaxPlanPages <= 0)
        {
            failures.Add($"{section}: timeouts, budgets and page limits must be positive.");
        }

        if (options.ClaimLeaseSeconds <= options.CallBudgetSeconds + options.ReconcileBudgetSeconds)
        {
            failures.Add($"{section}:ClaimLeaseSeconds must exceed CallBudgetSeconds + ReconcileBudgetSeconds.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
