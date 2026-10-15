using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. Values come from configuration only
/// (user-secrets in development; environment variables such as <c>Maxio__ApiKey</c> or a secret store elsewhere).
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (sent as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site; the API base address is derived from it unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are the plans offered to shoppers.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — optional; how new subscriptions are collected. Defaults to <c>remittance</c>
    /// (Maxio invoices the shopper) because this storefront captures no card; <c>automatic</c> requires a payment profile.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    public const string DefaultPaymentCollectionMethod = "remittance";
}

/// <summary>Fails host startup (with <c>ValidateOnStart</c>) when a required setting is missing. Messages name keys, never values.</summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, an environment variable (Maxio__ApiKey) or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when it is set.");
            }
        }
        else if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
        }

        if (!string.IsNullOrWhiteSpace(options.Environment)
            && !string.Equals(options.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be 'US' or 'EU' when it is set.");
        }

        if (!string.IsNullOrWhiteSpace(options.PaymentCollectionMethod)
            && !CollectionMethod.TryGetKnownValue(options.PaymentCollectionMethod.Trim(), out _))
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of: automatic, remittance, prepaid, invoice.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
