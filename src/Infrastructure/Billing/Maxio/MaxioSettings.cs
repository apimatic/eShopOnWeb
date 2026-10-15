using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from user-secrets (development) or the
/// environment (<c>Maxio__ApiKey</c> etc., or the <c>MAXIO_*</c> variables mapped in Program.cs) — never from appsettings.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (HTTP Basic username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; the API base address is derived from it unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override: when set, used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Refuses to start the host on a missing or blank Maxio setting. Messages name the key, never the value.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)} is not configured. Set it via user-secrets or the environment.");
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
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)} must be an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
