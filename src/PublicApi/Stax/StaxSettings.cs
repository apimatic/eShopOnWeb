namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Bound from the "Stax:" configuration section (user-secrets / environment). No values live in the repository.</summary>
public class StaxSettings
{
    public const string SECTION = "Stax";
    public const string DEFAULT_BASE_URL = "https://apiprod.fattlabs.com";

    public string? ApiKey { get; set; }
    public string? PlatformApiKey { get; set; }
    public string? BaseUrl { get; set; }
}
