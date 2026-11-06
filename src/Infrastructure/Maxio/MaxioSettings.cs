using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the "Maxio" configuration section. Values come from user-secrets / host configuration only.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional. When set it is used verbatim as the API base address instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Fails host start-up (naming the keys, never the values) when the Maxio settings are unusable.</summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:ApiKey is not configured (use user-secrets or host configuration).");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            failures.Add($"{MaxioSettings.CONFIG_NAME}:BaseUrl must be an absolute URI when set.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
