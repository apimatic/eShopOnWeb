using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Fail-fast validation for <see cref="MaxioOptions"/>: the host refuses to start
/// when a required setting is missing or blank, so a misconfigured deployment fails
/// at startup rather than surfacing as an unauthenticated 401 on the first call.
/// Messages name the configuration key and never echo a value.
/// </summary>
public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{MaxioOptions.SectionName}:ApiKey is not configured (set MAXIO_API_KEY via user-secrets or environment).");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            failures.Add($"{MaxioOptions.SectionName}:Subdomain is not configured (set MAXIO_SITE_SUBDOMAIN via user-secrets or environment).");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add($"{MaxioOptions.SectionName}:ProductFamilyHandle is not configured (set MAXIO_DEFAULT_PRODUCT_FAMILY via user-secrets or environment).");

        if (!string.IsNullOrWhiteSpace(options.Environment)
            && !string.Equals(options.Environment, "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{MaxioOptions.SectionName}:Environment must be \"US\" or \"EU\".");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            failures.Add($"{MaxioOptions.SectionName}:BaseUrl must be an absolute URL when set.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}