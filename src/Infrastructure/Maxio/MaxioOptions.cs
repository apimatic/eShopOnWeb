using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string HttpClientName = "MaxioApiClient";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                $"Maxio:{nameof(Subdomain)} is required when Maxio:{nameof(BaseUrl)} is not set.");
        }
        return new Uri($"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com/");
    }
}

public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"Maxio:{nameof(MaxioOptions.ApiKey)} must be configured.");
        }
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"Maxio:{nameof(MaxioOptions.ProductFamilyHandle)} must be configured.");
        }
        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add($"Either Maxio:{nameof(MaxioOptions.BaseUrl)} or Maxio:{nameof(MaxioOptions.Subdomain)} must be configured.");
        }
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
