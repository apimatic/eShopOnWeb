using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Refuses to start the host with incomplete Maxio settings. Messages name the configuration key, never its value.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        RequireValue(failures, settings.ApiKey, nameof(MaxioSettings.ApiKey));
        RequireValue(failures, settings.Subdomain, nameof(MaxioSettings.Subdomain));
        RequireValue(failures, settings.ProductFamilyHandle, nameof(MaxioSettings.ProductFamilyHandle));

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl)
            && (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)))
        {
            failures.Add($"{Key(nameof(MaxioSettings.BaseUrl))} must be an absolute http(s) URL when set.");
        }

        if (!ServerEnvironment.TryGetKnownValue(settings.Environment, out _))
        {
            failures.Add($"{Key(nameof(MaxioSettings.Environment))} must be 'US' or 'EU'.");
        }

        if (!CollectionMethod.TryGetKnownValue(settings.PaymentCollectionMethod, out _))
        {
            failures.Add($"{Key(nameof(MaxioSettings.PaymentCollectionMethod))} must be one of 'remittance', 'invoice', 'automatic', 'prepaid'.");
        }

        RequireRange(failures, settings.AttemptTimeoutSeconds, 1, 120, nameof(MaxioSettings.AttemptTimeoutSeconds));
        RequireRange(failures, settings.CallBudgetSeconds, 1, 300, nameof(MaxioSettings.CallBudgetSeconds));
        RequireRange(failures, settings.PlanCacheSeconds, 0, 86400, nameof(MaxioSettings.PlanCacheSeconds));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireValue(List<string> failures, string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{Key(key)} is not configured. Set it via user-secrets, the environment (Maxio__{key}) or your secret store.");
        }
    }

    private static void RequireRange(List<string> failures, int value, int min, int max, string key)
    {
        if (value < min || value > max)
        {
            failures.Add($"{Key(key)} must be between {min} and {max}.");
        }
    }

    private static string Key(string property) => $"{MaxioSettings.SectionName}:{property}";
}
