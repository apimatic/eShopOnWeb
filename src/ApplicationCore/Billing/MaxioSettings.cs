using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration
/// (environment variables or user-secrets) - never commit them.
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing (Chargify) API key. Used as the Basic-auth username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional verbatim API base address. When set it is used instead of deriving
    /// one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>API handle of the product family that contains the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Resolves the API base address: an explicit override wins, otherwise the server
    /// template from the Maxio OpenAPI specification (https://{site}.chargify.com) is used.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set 'Maxio:BaseUrl' or 'Maxio:Subdomain' (environment variable MAXIO_SITE_SUBDOMAIN).");

        return $"https://{Subdomain!.Trim()}.chargify.com";
    }

    public void ValidateRequiredSettings()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set 'Maxio:ApiKey' (environment variable MAXIO_API_KEY).");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set 'Maxio:ProductFamilyHandle' (environment variable MAXIO_DEFAULT_PRODUCT_FAMILY).");

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set 'Maxio:BaseUrl' or 'Maxio:Subdomain' (environment variable MAXIO_SITE_SUBDOMAIN).");
    }
}
