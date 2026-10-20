using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. Values come from user-secrets,
/// environment variables (<c>Maxio__ApiKey</c>, ...) or a secret store, never from a committed file.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c>: the Maxio API key (HTTP Basic username; the password is the constant <c>x</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c>: the site subdomain; the base address is derived from it unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c>: handle of the product family whose products are the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c>: optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public bool HasBaseUrlOverride => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>
/// Startup validation: the host refuses to start when a required Maxio setting is missing or blank.
/// Messages name the configuration key and never echo a value.
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

        if (options.HasBaseUrlOverride)
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)} must be an absolute http(s) URL when set.");
            }
        }
        else if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)} is not configured (required unless {MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)} is set).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
