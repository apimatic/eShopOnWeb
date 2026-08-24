namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the Basic-auth username; password is literally "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Subdomain of the Maxio site, e.g. "my-site" for https://my-site.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseAddress() =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.TrimEnd('/')
            : $"https://{Subdomain}.chargify.com";
}
