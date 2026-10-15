using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the <c>Maxio:</c> configuration section.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio (Chargify) API key. Keep in user-secrets / the deployment's secret store.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain; used to derive the API base address unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        // Messages name the config keys only; values are never echoed.
        var failures = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:ApiKey is not configured. Set it via user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:BaseUrl must be an absolute URL when set.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
