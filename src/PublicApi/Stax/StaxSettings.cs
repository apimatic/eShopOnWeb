namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Bound from the "Stax:" configuration section.</summary>
public class StaxSettings
{
    public const string SectionName = "Stax";

    /// <summary>Public Stax API address, used when Stax:BaseUrl is not set.</summary>
    public const string DefaultBaseUrl = "https://apiprod.fattlabs.com";

    public string? ApiKey { get; set; }
    public string? PlatformApiKey { get; set; }
    public string? BaseUrl { get; set; }
}
