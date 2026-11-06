namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
