using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// user-secrets or environment variables; none are stored in the repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method requested at subscription creation. Defaults to "remittance"
    /// (invoice billing) so signups succeed without a card on file; use "invoice" on legacy
    /// statement-based sites or "automatic" when a payment profile is collected.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public string GetBaseUrl() =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.TrimEnd('/')
            : $"https://{Subdomain}.chargify.com";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio billing is not configured: '{CONFIG_NAME}:ApiKey' is missing. " +
                "Provide it via user-secrets or the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio billing is not configured: set '{CONFIG_NAME}:BaseUrl' or '{CONFIG_NAME}:Subdomain' " +
                "(the latter via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Maxio billing is not configured: '{CONFIG_NAME}:ProductFamilyHandle' is missing. " +
                "Provide it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }
}
