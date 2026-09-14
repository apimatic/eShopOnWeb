namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values come from environment/user-secrets;
/// none are hard-coded so the same build can target a different Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_SECTION = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of the
    /// address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
