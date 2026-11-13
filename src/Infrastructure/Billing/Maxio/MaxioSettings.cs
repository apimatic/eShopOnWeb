using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. No value has a default: every deployment supplies
/// its own site and catalog (user-secrets in Development, environment/secret store elsewhere, e.g. <c>Maxio__ApiKey</c>).
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Maxio API key (HTTP Basic username). Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain; the API host is derived from it.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — handle of the product family whose products are the offered plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional. When set, used verbatim as the API base address instead of the subdomain-derived host.</summary>
    public string? BaseUrl { get; set; }

    public bool HasBaseUrlOverride => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>
/// Refuses to start the host when a Maxio setting is missing or blank. Messages name the key, never the value.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it with user-secrets, an environment variable (Maxio__ApiKey) or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (settings.HasBaseUrlOverride)
        {
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when it is set.");
            }
        }
        else if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
