using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. Supply them from user-secrets in development
/// and from environment variables (<c>Maxio__ApiKey</c>, ...) or a secret store elsewhere — never from
/// appsettings files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (sent as the Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site; the API host is derived from it unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose plans this store offers.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set, used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    public bool IsEuHosted => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Refuses to start the host when a required Maxio setting is missing. Messages name the key, never the value.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioOptions.SectionName}:ApiKey is not configured. Set it with user-secrets or the Maxio__ApiKey environment variable.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{MaxioOptions.SectionName}:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add($"{MaxioOptions.SectionName}:Subdomain is not configured (required unless {MaxioOptions.SectionName}:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback))
        {
            failures.Add($"{MaxioOptions.SectionName}:BaseUrl must be an absolute https URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.Environment) && !options.IsEuHosted &&
            !string.Equals(options.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{MaxioOptions.SectionName}:Environment must be US or EU.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
