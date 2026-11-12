namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Bound from the "Maxio:" configuration section.</summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }
}
