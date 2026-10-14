using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. Values come from user-secrets in development
/// and from environment variables / a secret store in deployed environments — never from appsettings files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (HTTP Basic username).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain the API base address is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — how new subscriptions are collected. This flow captures no payment
    /// method, so the default is <c>remittance</c> (invoiced); a legacy Statements site needs <c>invoice</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary><c>Maxio:AttemptTimeoutSeconds</c> — bound on a single HTTP attempt.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary><c>Maxio:OperationBudgetSeconds</c> — bound on everything one API request does against Maxio.</summary>
    public int OperationBudgetSeconds { get; set; } = 30;

    /// <summary><c>Maxio:PlanCacheSeconds</c> — how long the plan list is cached; 0 disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;
}

/// <summary>
/// Refuses to start the host on missing or malformed Maxio settings, rather than surfacing them as a 401 or a
/// bad host on the first shopper request. Messages name the key, never the value.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets or the environment before starting the app.");
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
            else if (Uri.CheckHostName(options.Subdomain.Trim()) == UriHostNameType.Unknown || options.Subdomain.Contains('.'))
            {
                failures.Add("Maxio:Subdomain must be a bare site subdomain, not a host name or URL.");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when set.");
        }

        if (!MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(options.PaymentCollectionMethod ?? string.Empty, out _))
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of remittance, automatic, prepaid or invoice.");
        }

        if (options.AttemptTimeoutSeconds <= 0)
        {
            failures.Add("Maxio:AttemptTimeoutSeconds must be positive.");
        }

        if (options.OperationBudgetSeconds < options.AttemptTimeoutSeconds)
        {
            failures.Add("Maxio:OperationBudgetSeconds must be at least Maxio:AttemptTimeoutSeconds.");
        }

        if (options.PlanCacheSeconds < 0)
        {
            failures.Add("Maxio:PlanCacheSeconds must not be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
