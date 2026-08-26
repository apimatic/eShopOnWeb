using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied through user-secrets or environment variables; none are stored in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key. Used as the Basic-auth username (password is literally "x"),
    /// per the OpenAPI spec's BasicAuth security scheme.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Advanced Billing site subdomain (the {site} server variable in the OpenAPI spec).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// API handle of the product family that holds the subscription plans on offer.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving the address from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Advanced Billing hosting environment: "US" (default) or "EU". Drives server templating
    /// per the spec's x-server-configuration: US -> https://{site}.chargify.com,
    /// EU -> https://{site}.ebilling.maxio.com. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = "US";

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        var host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return new Uri($"https://{host}/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: 'Maxio:ApiKey' is missing. Provide it via the MAXIO_API_KEY environment variable loaded into .NET user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: 'Maxio:Subdomain' is missing (or set 'Maxio:BaseUrl' explicitly). Provide it via the MAXIO_SITE_SUBDOMAIN environment variable loaded into .NET user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing. Provide it via the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable loaded into .NET user-secrets.");
        }
    }
}
