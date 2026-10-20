using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Refuses to let the host start with incomplete Maxio settings. Messages name the missing key and
/// never echo a configured value.
/// </summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, the environment or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                failures.Add("Maxio:Subdomain is not configured (it is required unless Maxio:BaseUrl is set).");
            }
            else if (settings.Subdomain.Trim().IndexOfAny(new[] { '/', ':', '.', ' ' }) >= 0)
            {
                failures.Add("Maxio:Subdomain must be the bare site subdomain, not a host name or URL.");
            }
        }
        else if (!Uri.TryCreate(settings.BaseUrl.Trim(), UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when it is set.");
        }

        if (!string.IsNullOrWhiteSpace(settings.Environment)
            && !string.Equals(settings.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be US or EU when it is set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
