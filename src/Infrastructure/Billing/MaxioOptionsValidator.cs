using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return ValidateOptionsResult.Fail("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return ValidateOptionsResult.Fail("Maxio:ProductFamilyHandle is required.");
        }

        var baseUrl = options.ResolveBaseUrl();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("The Maxio API base URL must be an absolute HTTPS URL.");
        }

        return ValidateOptionsResult.Success;
    }
}
