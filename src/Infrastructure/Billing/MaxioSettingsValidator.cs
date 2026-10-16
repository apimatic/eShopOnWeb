using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Refuses to let the host start with incomplete Maxio settings. Messages name the missing key and never echo a value.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, the Maxio__ApiKey environment variable, or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }

        if (!string.IsNullOrWhiteSpace(settings.Environment)
            && !string.Equals(settings.Environment, "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be US or EU.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
