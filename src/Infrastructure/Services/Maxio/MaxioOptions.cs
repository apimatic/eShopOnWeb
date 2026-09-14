namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Populate via user-secrets/environment
/// variables in every environment - never commit real values to appsettings*.json.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key, used as the HTTP Basic Auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "cp-exp-3". Used to derive the API base URL.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of
    /// deriving "https://{Subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
