using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;

        return new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
    }
}

public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return ValidateOptionsResult.Fail("Maxio:ProductFamilyHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return ValidateOptionsResult.Fail("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)))
        {
            return ValidateOptionsResult.Fail("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) &&
            (options.Subdomain.Contains('/') || options.Subdomain.Contains(':')))
        {
            return ValidateOptionsResult.Fail("Maxio:Subdomain must be a site subdomain, not a URL.");
        }

        return ValidateOptionsResult.Success;
    }
}
