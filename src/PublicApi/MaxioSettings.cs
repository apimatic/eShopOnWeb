namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// The API key for Maxio Advanced Billing.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The site subdomain (e.g. "cp-exp-7").
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The default product family handle for subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base URL.
    /// When set, used verbatim instead of deriving from subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
