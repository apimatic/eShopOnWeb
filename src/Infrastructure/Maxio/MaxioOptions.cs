namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio"
/// configuration section; values come from user-secrets / environment, never
/// from the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key, used as the Basic-auth username (password is always "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Advanced Billing site subdomain, used to derive the API base URL.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address; when set, it is used verbatim
    /// instead of a URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Advanced Billing environment ("US" or "EU") used when deriving the base URL
    /// from the subdomain. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = "US";
}
