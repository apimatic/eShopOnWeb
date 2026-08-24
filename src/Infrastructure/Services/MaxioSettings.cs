namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Binds the "Maxio" configuration section. Values are supplied via user-secrets /
/// environment, never from files in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base-address override; wins over <see cref="Subdomain"/> when set.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
