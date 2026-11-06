namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings read from the <c>Maxio:</c> configuration section.
/// Credentials belong in user-secrets / environment configuration, never in source control.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
