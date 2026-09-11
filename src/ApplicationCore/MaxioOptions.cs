namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Configuration options for the Maxio Advanced Billing API integration.
/// </summary>
public class MaxioOptions
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>
    /// The API key for authenticating with the Maxio Billing API.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The subdomain for the Maxio site (e.g., "cp-exp-8").
    /// Used to construct the base URL: https://{subdomain}.chargify.com
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base URL.
    /// When set, this is used verbatim instead of deriving from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The default product family handle for subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Gets the effective base URL for the Maxio API.
    /// If BaseUrl is set, returns that; otherwise derives from Subdomain.
    /// </summary>
    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
