namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio settings, bound from the "Maxio" configuration section.
/// Credentials come from .NET user-secrets (or any other configuration provider), never from source control.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (Maxio:ApiKey).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (Maxio:Subdomain).</summary>
    public string? Subdomain { get; set; }

    /// <summary>"US" or "EU" (Maxio:Environment).</summary>
    public string? Environment { get; set; }

    /// <summary>Optional override of the API base address, used verbatim (Maxio:BaseUrl).</summary>
    public string? BaseUrl { get; set; }
}
