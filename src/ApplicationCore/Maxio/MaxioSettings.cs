using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional subscription payment_collection_method (see spec Collection-Method schema).
    /// Defaults to "remittance" so subscribe works without card capture / 3-DS, matching the
    /// seeded sandbox plans. Set to "automatic" for catalogs whose plans require a stored method.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = MaxioCollectionMethods.Remittance;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl or Maxio:Subdomain must be configured to derive the Advanced Billing API base address.");
        }

        return string.Format("https://{0}.chargify.com", Subdomain.Trim());
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        ResolveBaseUrl();
    }
}
