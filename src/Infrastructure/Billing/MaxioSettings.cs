namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// user-secrets or environment variables — never committed to source.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
