using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Populate via user-secrets or environment variables; never commit real values.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Billing API key (used as the Basic-auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "mysite" for mysite.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that contains the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: set either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain'.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: '{SectionName}:ApiKey' is missing. " +
                "Set it via user-secrets or the MAXIO_API_KEY environment variable.");
        }

        _ = GetBaseAddress();

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: '{SectionName}:ProductFamilyHandle' is missing. " +
                "Set it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }
}
