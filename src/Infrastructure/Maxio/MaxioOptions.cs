using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing API. Bound from the "Maxio" configuration
/// section; values are supplied via user-secrets / environment variables, never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method used at signup so shoppers can subscribe without card capture:
    /// "remittance" on Relationship Invoicing sites, "invoice" on legacy statement-based sites.
    /// Defaults to "remittance".
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Resolves the API base address. Per the Advanced Billing docs, site APIs are served at
    /// https://{subdomain}.chargify.com unless an explicit BaseUrl override is configured.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set Maxio:BaseUrl or Maxio:Subdomain (user-secrets / environment).");
        }

        return $"https://{Subdomain.TrimEnd('.')}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio is not configured: Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio is not configured: Maxio:ProductFamilyHandle is required.");
        }
    }
}
