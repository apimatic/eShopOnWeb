using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails fast, at first use, on a Maxio configuration that cannot possibly work,
/// instead of surfacing it later as an opaque 401 from the provider.
/// </summary>
public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{MaxioOptions.SectionName}:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{MaxioOptions.SectionName}:ProductFamilyHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add($"{MaxioOptions.SectionName}:Subdomain is required unless {MaxioOptions.SectionName}:BaseUrl is set.");
            }

            if (!MaxioOptions.IsKnownEnvironment(options.Environment))
            {
                failures.Add($"{MaxioOptions.SectionName}:Environment must be 'US' or 'EU'.");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{MaxioOptions.SectionName}:BaseUrl must be an absolute http(s) URL.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add($"{MaxioOptions.SectionName}:TimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaxRetryAttempts is < 0 or > 10)
        {
            failures.Add($"{MaxioOptions.SectionName}:MaxRetryAttempts must be between 0 and 10.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
