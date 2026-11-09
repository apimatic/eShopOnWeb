namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Settings from the "Stax:" configuration section.</summary>
public class StaxOptions
{
    public const string SECTION = "Stax";

    /// <summary>Merchant (sandbox) API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Partner/platform API key; only present when the shop has one.</summary>
    public string? PlatformApiKey { get; set; }

    /// <summary>Optional override of the API's base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }

    public const string DEFAULT_BASE_URL = "https://apiprod.fattlabs.com";

    public string EffectiveBaseUrl => string.IsNullOrWhiteSpace(BaseUrl) ? DEFAULT_BASE_URL : BaseUrl;
}
