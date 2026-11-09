namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Bound from the "Stax:" configuration section.</summary>
public class StaxOptions
{
    public const string SectionName = "Stax";
    public const string DefaultBaseUrl = "https://apiprod.fattlabs.com";

    /// <summary>Merchant API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Partner (platform) API key; only needed to onboard sellers.</summary>
    public string? PlatformApiKey { get; set; }

    /// <summary>Optional override of the API base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
