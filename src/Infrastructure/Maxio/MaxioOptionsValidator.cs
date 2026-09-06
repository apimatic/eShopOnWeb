using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails fast at start-up when the Maxio configuration is incomplete, rather than at the first
/// shopper request. Only names of the missing settings are reported - never their values.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}' is required unless '{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)}' is set.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}' is required.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)}' must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && !MaxioOptions.IsKnownEnvironment(options.Environment))
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.Environment)}' must be '{MaxioOptions.UsEnvironment}' or '{MaxioOptions.EuEnvironment}'.");
        }

        if (!Array.Exists(MaxioOptions.CollectionMethods, method => string.Equals(method, options.PaymentCollectionMethod, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.PaymentCollectionMethod)}' must be one of: {string.Join(", ", MaxioOptions.CollectionMethods)}.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.Timeout)}' must be greater than zero.");
        }

        if (options.MaxRetryAttempts < 0)
        {
            failures.Add($"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.MaxRetryAttempts)}' cannot be negative.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
