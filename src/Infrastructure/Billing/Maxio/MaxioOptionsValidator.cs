using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Fails fast, and with an actionable message, when the Maxio section is missing or wrong.
/// </summary>
/// <remarks>
/// Validation runs the first time the options are resolved rather than at startup, so a
/// deployment without billing configured still serves the rest of the API; the
/// subscription endpoints are the only ones that surface the misconfiguration.
/// </remarks>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    private static readonly Regex SubdomainPattern = new("^[A-Za-z0-9][A-Za-z0-9-]*$", RegexOptions.Compiled);

    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"'{MaxioOptions.SectionName}:ApiKey' is required (set it from MAXIO_API_KEY, e.g. via dotnet user-secrets).");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"'{MaxioOptions.SectionName}:ProductFamilyHandle' is required (set it from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add($"'{MaxioOptions.SectionName}:Subdomain' is required unless '{MaxioOptions.SectionName}:BaseUrl' is set (set it from MAXIO_SITE_SUBDOMAIN).");
            }
            else if (!SubdomainPattern.IsMatch(options.Subdomain!.Trim()))
            {
                failures.Add($"'{MaxioOptions.SectionName}:Subdomain' must be a bare site subdomain such as 'my-site', not a URL.");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl!.Trim(), UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"'{MaxioOptions.SectionName}:BaseUrl' must be an absolute http or https URL.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add($"'{MaxioOptions.SectionName}:TimeoutSeconds' must be greater than zero.");
        }

        if (options.MaxRetryAttempts < 0)
        {
            failures.Add($"'{MaxioOptions.SectionName}:MaxRetryAttempts' cannot be negative.");
        }

        if (options.RetryBaseDelayMilliseconds < 0)
        {
            failures.Add($"'{MaxioOptions.SectionName}:RetryBaseDelayMilliseconds' cannot be negative.");
        }

        return failures.Any()
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
