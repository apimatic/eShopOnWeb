using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Validates that the Maxio settings required to reach the billing system are present and coherent.
/// Runs at startup (ValidateOnStart) so misconfiguration fails fast rather than at first request.
/// </summary>
public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"'{MaxioOptions.SectionName}:ApiKey' is required.");
        }

        // A base address must be resolvable: either an explicit BaseUrl, or a Subdomain to derive one.
        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add($"Either '{MaxioOptions.SectionName}:Subdomain' or '{MaxioOptions.SectionName}:BaseUrl' is required.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add($"'{MaxioOptions.SectionName}:BaseUrl' must be an absolute URL when set.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"'{MaxioOptions.SectionName}:ProductFamilyHandle' is required.");
        }

        if (string.IsNullOrWhiteSpace(options.PaymentCollectionMethod))
        {
            failures.Add($"'{MaxioOptions.SectionName}:PaymentCollectionMethod' must not be blank.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
