using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Nothing here has a default value: every deployment supplies its own.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        // Messages name the configuration key only, never a value.
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return ValidateOptionsResult.Fail("Maxio:ApiKey is not configured (set it with user-secrets or the environment).");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            return ValidateOptionsResult.Fail("Maxio:ProductFamilyHandle is not configured.");

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return ValidateOptionsResult.Fail("Maxio:BaseUrl must be an absolute http(s) URL when set.");
        }
        else if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return ValidateOptionsResult.Fail("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
        }

        return ValidateOptionsResult.Success;
    }
}
