using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. Secrets come from user-secrets in
/// development and from environment variables / a secret store elsewhere — never from appsettings.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Maxio API key (HTTP Basic username). Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain the API base address is derived from.
    /// Required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — handle of the product family whose products are the
    /// subscription plans on offer. Required.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional. When set it is used verbatim as the API base address
    /// instead of the address derived from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional Maxio hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    public bool IsEuRegion => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Refuses to start the host when a required Maxio setting is missing or malformed, instead of
/// letting the first shopper discover it as a 401 or a call to the wrong host. Never echoes values.
/// </summary>
public sealed partial class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
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

        var hasBaseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        var hasSubdomain = !string.IsNullOrWhiteSpace(options.Subdomain);

        if (!hasBaseUrl && !hasSubdomain)
        {
            failures.Add("Maxio:Subdomain is not configured (and no Maxio:BaseUrl override is set).");
        }

        if (hasSubdomain && !SubdomainPattern().IsMatch(options.Subdomain!.Trim()))
        {
            failures.Add("Maxio:Subdomain must be a bare site subdomain (letters, digits and hyphens), not a URL or host name.");
        }

        if (hasBaseUrl && !IsAcceptableBaseUrl(options.BaseUrl!))
        {
            failures.Add("Maxio:BaseUrl must be an absolute https URL (http is accepted only for loopback hosts).");
        }

        if (!string.IsNullOrWhiteSpace(options.Environment)
            && !string.Equals(options.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be US or EU when set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAcceptableBaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }
        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$")]
    private static partial Regex SubdomainPattern();
}
