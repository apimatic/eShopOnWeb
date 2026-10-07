using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails fast at startup when required Maxio settings are missing.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)} is required (set via user-secrets or MAXIO_API_KEY).");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add($"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)} is required (set via user-secrets or MAXIO_SITE_SUBDOMAIN).");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} is required (set via user-secrets or MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
