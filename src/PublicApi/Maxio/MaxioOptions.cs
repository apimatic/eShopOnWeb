using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: either '{SectionName}:{nameof(BaseUrl)}' or '{SectionName}:{nameof(Subdomain)}' must be set.");
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"Maxio configuration is incomplete: '{SectionName}:{nameof(ApiKey)}' is required.");
        }

        ResolveBaseUrl();

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException($"Maxio configuration is incomplete: '{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }
    }
}
