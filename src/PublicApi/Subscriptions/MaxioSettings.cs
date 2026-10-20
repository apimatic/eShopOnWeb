using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}

public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:ApiKey is not configured. Set it via user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:ProductFamilyHandle is not configured.");
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
                failures.Add($"{MaxioSettings.CONFIG_NAME}:Subdomain is not configured (required unless {MaxioSettings.CONFIG_NAME}:BaseUrl is set).");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{MaxioSettings.CONFIG_NAME}:BaseUrl must be an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
