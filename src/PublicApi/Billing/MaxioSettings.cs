using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. Values come from user-secrets,
/// environment variables or a secret store, never from a file in this repository.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> - the Maxio API key (Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> - the Maxio site subdomain the API base address is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> - the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> - optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public bool HasBaseUrlOverride => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>
/// Startup validation for <see cref="MaxioSettings"/>. Messages name the missing key, never a value.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it with user-secrets, the Maxio__ApiKey or MAXIO_API_KEY environment variable, or a secret store.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured. Set it with user-secrets, the Maxio__ProductFamilyHandle or MAXIO_DEFAULT_PRODUCT_FAMILY environment variable, or a secret store.");
        }

        if (options.HasBaseUrlOverride)
        {
            if (!IsAbsoluteHttpUrl(options.BaseUrl!))
            {
                failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when it is set.");
            }
        }
        else if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured (and no Maxio:BaseUrl override is set). Set it with user-secrets, the Maxio__Subdomain or MAXIO_SITE_SUBDOMAIN environment variable, or a secret store.");
        }
        else if (!IsSubdomainLabel(options.Subdomain!))
        {
            failures.Add("Maxio:Subdomain must be a bare site subdomain (letters, digits and hyphens only), not a URL or host name.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAbsoluteHttpUrl(string value)
    {
        // A "{site}" template placeholder is legal in a base URL; validate the rest of it.
        var candidate = value.Trim().Replace("{site}", "site", StringComparison.Ordinal);
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static bool IsSubdomainLabel(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is > 0 and <= 63 && trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }
}
