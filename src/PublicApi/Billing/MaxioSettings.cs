namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio settings, bound from the "Maxio" configuration section.
/// Values come from configuration (user-secrets / environment); none is hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio site API key (Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Maxio environment: "US" or "EU".</summary>
    public string? Environment { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
