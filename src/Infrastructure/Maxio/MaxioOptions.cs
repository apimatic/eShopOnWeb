namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings bound from the "Maxio:" configuration section. Values are never hard-coded;
/// they come from user-secrets / environment variables at deployment time.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio Advanced Billing API key (Basic auth username).
    /// </summary>
    public string ApiKey { get; set; } = default!;

    /// <summary>
    /// The site subdomain. Required unless BaseUrl is set.
    /// </summary>
    public string Subdomain { get; set; } = default!;

    /// <summary>
    /// Handle of the product family that holds the subscription plans offered by the store.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = default!;

    /// <summary>
    /// Optional verbatim API base address override; when set it replaces the URL derived from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}