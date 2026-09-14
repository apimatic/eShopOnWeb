using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Subdomain { get; set; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public Uri GetBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;

        return new Uri($"{baseUrl!.TrimEnd('/')}/", UriKind.Absolute);
    }
}

public sealed partial class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return ValidateOptionsResult.Fail("Maxio:ApiKey is required.");
        if (string.IsNullOrWhiteSpace(options.Subdomain) || !SubdomainPattern().IsMatch(options.Subdomain))
            return ValidateOptionsResult.Fail("Maxio:Subdomain must be a valid site subdomain.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            return ValidateOptionsResult.Fail("Maxio:ProductFamilyHandle is required.");

        try
        {
            var baseUri = options.GetBaseUri();
            if (baseUri.Scheme != Uri.UriSchemeHttps)
                return ValidateOptionsResult.Fail("Maxio:BaseUrl must use HTTPS.");
        }
        catch (UriFormatException)
        {
            return ValidateOptionsResult.Fail("Maxio:BaseUrl must be an absolute URL.");
        }

        return ValidateOptionsResult.Success;
    }

    [GeneratedRegex("^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$")]
    private static partial Regex SubdomainPattern();
}
