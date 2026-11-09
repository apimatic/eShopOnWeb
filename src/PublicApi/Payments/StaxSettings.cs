namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Stax settings, bound from the "Stax:" configuration section (user-secrets in development).
/// </summary>
public class StaxSettings
{
    public const string SECTION = "Stax";

    /// <summary>API key of the shop's own Stax merchant account.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Key of the Stax platform account; only needed to onboard sellers.</summary>
    public string? PlatformApiKey { get; set; }

    /// <summary>Optional override of the Stax API base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
