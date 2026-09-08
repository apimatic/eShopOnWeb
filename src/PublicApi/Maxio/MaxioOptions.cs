using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
///
/// The values are bound from the "Maxio" configuration section (keys <c>ApiKey</c>,
/// <c>Subdomain</c>, <c>ProductFamilyHandle</c> and the optional <c>BaseUrl</c> override).
/// <c>ApiKey</c>, <c>Subdomain</c> and <c>ProductFamilyHandle</c> fall back to the matching
/// <c>MAXIO_*</c> environment variables so credentials are never committed to the repository.
/// No value is hard-coded: the same build can target any Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    public const string API_KEY_ENV_VAR = "MAXIO_API_KEY";
    public const string SUBDOMAIN_ENV_VAR = "MAXIO_SITE_SUBDOMAIN";
    public const string PRODUCT_FAMILY_ENV_VAR = "MAXIO_DEFAULT_PRODUCT_FAMILY";

    /// <summary>The Billing API key. HTTP Basic auth username; the password is always "x".</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Billing API site subdomain (e.g. "cp-exp-8").</summary>
    public string? Subdomain { get; set; }

    /// <summary>The API handle of the product family that contains the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the Billing API base address. When set, it is used verbatim;
    /// otherwise the address is derived from <see cref="Subdomain"/> as
    /// <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Returns the Billing API base address, honoring the optional <see cref="BaseUrl"/> override.
    /// </summary>
    public Uri GetApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        EnsureConfigured();
        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }

    /// <summary>
    /// Verifies that the settings required to talk to Maxio are present. Throws a
    /// <see cref="MaxioConfigurationException"/> describing exactly which configuration key or
    /// environment variable is missing.
    /// </summary>
    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                $"The Maxio API key is not configured. Set the '{CONFIG_SECTION_NAME}:{nameof(ApiKey)}' " +
                $"configuration value (e.g. via user secrets or the '{API_KEY_ENV_VAR}' environment variable).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                $"The Maxio product family handle is not configured. Set the " +
                $"'{CONFIG_SECTION_NAME}:{nameof(ProductFamilyHandle)}' configuration value " +
                $"(e.g. via user secrets or the '{PRODUCT_FAMILY_ENV_VAR}' environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                $"The Maxio site subdomain is not configured. Set the " +
                $"'{CONFIG_SECTION_NAME}:{nameof(Subdomain)}' configuration value (e.g. via user secrets or the " +
                $"'{SUBDOMAIN_ENV_VAR}' environment variable), or set the '{CONFIG_SECTION_NAME}:{nameof(BaseUrl)}' " +
                $"override with the full Billing API base address.");
        }
    }
}
