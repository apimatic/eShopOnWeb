using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from configuration only (user-secrets in
/// development, environment / secret store elsewhere) — never from source.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (sent as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, e.g. the <c>{site}</c> in <c>https://{site}.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Startup validation: the host refuses to start with missing or blank Maxio settings rather than failing with
/// a 401 on the first request. Messages name the key, never the value.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioSettings.SectionName}:ApiKey is not configured. Set it via user-secrets, environment or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{MaxioSettings.SectionName}:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add($"{MaxioSettings.SectionName}:Subdomain is not configured (required unless {MaxioSettings.SectionName}:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{MaxioSettings.SectionName}:BaseUrl must be an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
