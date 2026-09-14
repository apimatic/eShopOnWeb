using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Validates the <c>Maxio</c> configuration section the first time the settings are resolved.
/// </summary>
/// <remarks>
/// Validation is deliberately not run at start-up: the rest of eShopOnWeb must keep working on a
/// host where subscription billing has not been configured. A misconfigured integration surfaces
/// as a clear failure on the subscription endpoints only.
/// </remarks>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("'Maxio:ApiKey' is required. Store it in user-secrets or the Maxio__ApiKey environment variable - never in a file in the repository.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("'Maxio:Subdomain' is required unless 'Maxio:BaseUrl' is set.");
        }

        if (hasBaseUrl)
        {
            if (!Uri.TryCreate(options.BaseUrl!.Trim(), UriKind.Absolute, out var baseUri))
            {
                failures.Add("'Maxio:BaseUrl' must be an absolute URL.");
            }
            else if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
            {
                failures.Add("'Maxio:BaseUrl' must use the http or https scheme.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("'Maxio:ProductFamilyHandle' is required; it selects the product family whose products are offered as subscription plans.");
        }

        if (!MaxioCollectionMethods.IsSupported(options.PaymentCollectionMethod))
        {
            failures.Add($"'Maxio:PaymentCollectionMethod' must be one of: {string.Join(", ", MaxioCollectionMethods.All)}.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add("'Maxio:TimeoutSeconds' must be greater than zero.");
        }

        if (options.MaxRetryAttempts < 1)
        {
            failures.Add("'Maxio:MaxRetryAttempts' must be at least 1.");
        }

        if (options.CatalogCacheSeconds < 0)
        {
            failures.Add("'Maxio:CatalogCacheSeconds' cannot be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
