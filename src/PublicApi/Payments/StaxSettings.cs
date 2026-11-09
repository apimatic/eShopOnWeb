namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The <c>Stax:</c> configuration section. The values come from configuration (user-secrets in
/// development, environment variables such as <c>Stax__ApiKey</c> elsewhere) and are never stored in the repository.
/// </summary>
public class StaxSettings
{
    public const string SectionName = "Stax";

    /// <summary>The shop's Stax API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Stax platform key, when the shop has one (needed to onboard sellers).</summary>
    public string? PlatformApiKey { get; set; }

    /// <summary>Optional override of the Stax API base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
