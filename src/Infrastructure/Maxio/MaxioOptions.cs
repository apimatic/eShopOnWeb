using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from environment
/// variables / user-secrets — never from source-controlled files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the Advanced Billing API base address
    /// instead of <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Configure Maxio:BaseUrl or Maxio:Subdomain to reach Advanced Billing.");
        }

        // Official US Advanced Billing host: https://{site}.chargify.com
        return $"https://{Subdomain.Trim()}.chargify.com/";
    }

    /// <summary>
    /// Product family identifier for list-products: numeric id or <c>handle:{handle}</c>.
    /// </summary>
    public string GetProductFamilyId()
    {
        var handle = (ProductFamilyHandle ?? string.Empty).Trim();
        if (handle.StartsWith("handle:", StringComparison.OrdinalIgnoreCase))
        {
            return handle;
        }

        return $"handle:{handle}";
    }

    public bool IsConfigured()
        => !string.IsNullOrWhiteSpace(ApiKey)
           && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
           && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));
}
