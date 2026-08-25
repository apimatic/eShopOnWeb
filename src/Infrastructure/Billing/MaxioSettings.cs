using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings for Maxio Advanced Billing, bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets / environment variables, never committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseUrl() =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.TrimEnd('/')
            : $"https://{Subdomain}.chargify.com";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it from the MAXIO_API_KEY environment variable via .NET user-secrets.");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Set it from the MAXIO_SITE_SUBDOMAIN environment variable via .NET user-secrets (or set Maxio:BaseUrl).");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable via .NET user-secrets.");
    }
}
