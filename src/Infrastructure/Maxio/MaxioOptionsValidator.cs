using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails fast on a Maxio configuration that could not possibly work, so a missing secret surfaces at
/// startup rather than as a 401 from the billing system on a shopper's first subscribe.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.ApiKey)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add(
                $"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.Subdomain)} is required unless " +
                $"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.BaseUrl)} is set.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} is required.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add($"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.TimeoutSeconds)} must be greater than zero.");
        }

        if (options.MaxRetryAttempts < 0)
        {
            failures.Add($"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.MaxRetryAttempts)} cannot be negative.");
        }

        if (options.CatalogCacheSeconds < 0)
        {
            failures.Add($"{MaxioOptions.ConfigurationSectionName}:{nameof(MaxioOptions.CatalogCacheSeconds)} cannot be negative.");
        }

        try
        {
            options.ResolveBaseAddress();
        }
        catch (InvalidOperationException ex)
        {
            failures.Add(ex.Message);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
