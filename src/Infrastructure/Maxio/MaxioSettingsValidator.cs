using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails startup with an actionable message when the Maxio section is missing or nonsensical,
/// rather than letting the first shopper discover it as a 500.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"'{MaxioSettings.SectionName}:ApiKey' is required. Set it with: dotnet user-secrets set \"{MaxioSettings.SectionName}:ApiKey\" <your-api-key>");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add($"'{MaxioSettings.SectionName}:Subdomain' is required unless '{MaxioSettings.SectionName}:BaseUrl' is set.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"'{MaxioSettings.SectionName}:ProductFamilyHandle' is required; it selects the catalog of subscription plans.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add($"'{MaxioSettings.SectionName}:BaseUrl' must be an absolute URI when set.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add($"'{MaxioSettings.SectionName}:TimeoutSeconds' must be greater than zero.");
        }

        if (options.MaxRetryAttempts < 0)
        {
            failures.Add($"'{MaxioSettings.SectionName}:MaxRetryAttempts' cannot be negative.");
        }

        if (options.MaxConcurrentRequests <= 0)
        {
            failures.Add($"'{MaxioSettings.SectionName}:MaxConcurrentRequests' must be greater than zero.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
