namespace Microsoft.eShopWeb.ApplicationCore.Settings;

public class PayPalOptions
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "Sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }
}