using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
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

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return string.IsNullOrWhiteSpace(options.Subdomain)
                ? ValidateOptionsResult.Fail("Maxio:Subdomain is required when Maxio:BaseUrl is not set.")
                : ValidateOptionsResult.Success;
        }

        return Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Maxio:BaseUrl must be an absolute URL when set.");
    }
}
