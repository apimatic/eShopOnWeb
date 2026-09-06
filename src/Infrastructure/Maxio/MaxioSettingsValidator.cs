using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails start-up when the Maxio credentials or catalog pointer are missing, so a misconfigured
/// deployment is caught before it serves a single request.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var section = MaxioSettings.ConfigurationSection;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail($"'{section}:{nameof(MaxioSettings.ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return ValidateOptionsResult.Fail($"'{section}:{nameof(MaxioSettings.ProductFamilyHandle)}' is required.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return ValidateOptionsResult.Fail(
                $"'{section}:{nameof(MaxioSettings.Subdomain)}' is required unless '{section}:{nameof(MaxioSettings.BaseUrl)}' is set.");
        }

        if (hasBaseUrl)
        {
            if (!Uri.TryCreate(options.BaseUrl!.Trim(), UriKind.Absolute, out var baseUri))
            {
                return ValidateOptionsResult.Fail($"'{section}:{nameof(MaxioSettings.BaseUrl)}' must be an absolute URL.");
            }

            if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
            {
                return ValidateOptionsResult.Fail($"'{section}:{nameof(MaxioSettings.BaseUrl)}' must use http or https.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
