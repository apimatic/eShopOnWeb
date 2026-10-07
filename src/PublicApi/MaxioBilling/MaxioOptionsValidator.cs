using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

/// <summary>
/// Validates <see cref="MaxioOptions"/> at startup: the API key and product
/// family handle are always required, and either a subdomain or an explicit
/// base URL must be provided.
/// </summary>
public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is required (set the MAXIO_API_KEY environment variable or a user secret).");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is required (set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or a user secret).");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Either Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl must be configured.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
