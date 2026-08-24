using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the Basic-auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Subdomain of the Maxio site, e.g. "mysite" for mysite.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain' must be provided.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"Maxio is not configured: '{SectionName}:ApiKey' is required.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain' is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException($"Maxio is not configured: '{SectionName}:ProductFamilyHandle' is required.");
        }
    }
}
