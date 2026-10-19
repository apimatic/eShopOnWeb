using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing settings, bound from the <c>Maxio:</c> configuration section.
/// Values come from configuration only (user-secrets in development, environment variables or a
/// secret store elsewhere) and are never committed to the repository.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Maxio API key (HTTP Basic user name). Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain. Required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — handle of the product family whose products are the offered plans. Required.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// <c>Maxio:BaseUrl</c> — optional override. When set, used as the API base address instead of the
    /// address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Startup validation for <see cref="MaxioSettings"/>: the host refuses to start when a required key
/// is missing or blank. Failure messages name the key, never its value.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)} is not configured. Set it via user-secrets, an environment variable or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)} is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)} is not configured (required unless {MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)} is set).");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)} must be an absolute http(s) URL when set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
