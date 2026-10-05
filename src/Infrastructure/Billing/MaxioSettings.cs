using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing settings, bound from the <c>Maxio</c> configuration section.
/// Values come from configuration only (user-secrets in development, environment variables or a secret store
/// elsewhere) — never from files in this repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Maxio API key (sent as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain; required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Refuses to start the host when the Maxio settings cannot work. Messages name keys, never values.</summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets or the Maxio__ApiKey environment variable.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                failures.Add("Maxio:Subdomain is not configured (required when Maxio:BaseUrl is not set).");
            }
        }
        else if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
