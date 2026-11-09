namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>The <c>Stax:</c> configuration section.</summary>
public class StaxOptions
{
    public const string SectionName = "Stax";

    /// <summary>Key of the shop's Stax merchant.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Key of the marketplace platform; only needed to onboard sellers.</summary>
    public string? PlatformApiKey { get; set; }

    /// <summary>Optional override of the API's base address, used verbatim.</summary>
    public string? BaseUrl { get; set; }
}
