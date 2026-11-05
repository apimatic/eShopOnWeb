using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio:" configuration section. Nothing here has a default value: the same build must run
/// against any Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (secret: user-secrets or environment only).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the subscription plans on offer.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Makes the host refuse to start when a required Maxio setting is missing or blank. Names keys, never values.</summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)} is not configured (set it via user-secrets or environment).");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)} is not configured.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)} is not configured.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !(Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)} must be an absolute http(s) URL when set.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
