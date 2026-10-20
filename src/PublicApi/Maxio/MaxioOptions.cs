using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. Values come from the environment
/// (user-secrets, environment variables, a secret store) and never from a file in the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> - the site's API key (HTTP Basic user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> - the site subdomain; the base address is derived from it.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> - the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> - optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Startup validation (registered with <c>ValidateOnStart</c>): the host refuses to start when a setting is
/// missing. Messages name the key, never the value.
/// </summary>
public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, an environment variable or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured (and no Maxio:BaseUrl override is set).");
        }

        if (hasBaseUrl &&
            (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)))
        {
            failures.Add("Maxio:BaseUrl is set but is not an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
