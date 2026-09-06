using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails fast at start-up when the Maxio configuration cannot produce a usable client, rather than
/// letting the first shopper discover it.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioSettings.SectionName}:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{MaxioSettings.SectionName}:ProductFamilyHandle is required.");
        }

        if (options.TimeoutSeconds is < 1 or > 600)
        {
            failures.Add($"{MaxioSettings.SectionName}:TimeoutSeconds must be between 1 and 600.");
        }

        if (options.MaxRetryAttempts is < 0 or > 10)
        {
            failures.Add($"{MaxioSettings.SectionName}:MaxRetryAttempts must be between 0 and 10.");
        }

        if (options.RetryBaseDelayMilliseconds is < 0 or > 60_000)
        {
            failures.Add($"{MaxioSettings.SectionName}:RetryBaseDelayMilliseconds must be between 0 and 60000.");
        }

        if (options.CatalogCacheSeconds is < 0 or > 86_400)
        {
            failures.Add($"{MaxioSettings.SectionName}:CatalogCacheSeconds must be between 0 and 86400.");
        }

        try
        {
            options.ResolveBaseAddress();
        }
        catch (InvalidOperationException ex)
        {
            failures.Add(ex.Message);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
