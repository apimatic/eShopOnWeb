using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (!string.IsNullOrWhiteSpace(Subdomain))
        {
            return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
        }

        throw new InvalidOperationException(
            "Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain.");
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

        _ = GetBaseAddress();
    }
}
