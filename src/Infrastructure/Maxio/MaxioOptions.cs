using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the <c>Maxio:</c> configuration section. No value has a default in the repository.</summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (secret). Supply through user-secrets or the deployment's secret store.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose plans this application offers.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override: when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        // Messages name the key only; they never echo a value.
        var failures = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)} is not configured.");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            failures.Add($"{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)} is not configured.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add($"{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} is not configured.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !(Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            failures.Add($"{MaxioOptions.SectionName}:{nameof(MaxioOptions.BaseUrl)} must be an absolute http(s) URL when set.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
