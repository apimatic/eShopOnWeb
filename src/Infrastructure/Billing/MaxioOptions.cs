using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Bound from configuration section "Maxio".</summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set it is used verbatim as the Maxio server base URL.</summary>
    public string? BaseUrl { get; set; }
}

public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        // Messages name the key only, never a value.
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, environment variable (Maxio__ApiKey / MAXIO_API_KEY) or your secret store.");

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add("Maxio:ProductFamilyHandle is not configured.");

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                failures.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }
        else if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
