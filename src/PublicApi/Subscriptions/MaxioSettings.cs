using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Bound from the "Maxio" configuration section. Values come from user-secrets / environment, never from the repo.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API base address instead of the one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Fails host startup when a required Maxio key is blank. Messages name keys only, never values.</summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{MaxioSettings.SectionName}:ApiKey is not configured. Set it via user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            failures.Add($"{MaxioSettings.SectionName}:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add($"{MaxioSettings.SectionName}:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !(Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            failures.Add($"{MaxioSettings.SectionName}:BaseUrl must be an absolute http(s) URL when set.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
