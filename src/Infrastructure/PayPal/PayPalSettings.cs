namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the "PayPal" configuration section. Values come from configuration
/// (user secrets / environment), never from code.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string Environment { get; set; } = "sandbox";
    public string? Currency { get; set; }
    public string? BaseUrl { get; set; }
}
