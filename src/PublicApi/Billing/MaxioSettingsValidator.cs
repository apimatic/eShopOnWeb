using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Refuses to start the host when a Maxio setting is missing. Messages name the configuration key only, never a value.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.ApiKey)} is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.ProductFamilyHandle)} is not configured.");
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.BaseUrl)} must be an absolute https URL.");
            }
        }
        else if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            failures.Add($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.Subdomain)} is not configured (or set {MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.BaseUrl)}).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
