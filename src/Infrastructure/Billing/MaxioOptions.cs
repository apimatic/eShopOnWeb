using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing settings, bound from the <c>Maxio</c> configuration section.
/// None of these values may be committed: supply them from user-secrets (development),
/// environment variables (<c>Maxio__ApiKey</c>, …) or a secret store.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site API key (sent as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain the API base address is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address instead of the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }
}

public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        // Messages name the missing key and never echo a value.
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, the Maxio__ApiKey environment variable or your secret store.");
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

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (!string.IsNullOrWhiteSpace(options.Environment)
            && !string.Equals(options.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be 'US' or 'EU' when it is set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
