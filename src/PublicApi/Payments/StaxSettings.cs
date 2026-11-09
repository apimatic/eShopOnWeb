namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The "Stax:" configuration section. No value is hard-coded; credentials come from user-secrets or the environment.
/// </summary>
public class StaxSettings
{
    public const string SECTION = "Stax";

    public string? ApiKey { get; set; }
    public string? PlatformApiKey { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How often pending bank payments and refunds are re-checked.</summary>
    public int ReconcileIntervalSeconds { get; set; } = 60;
}
