namespace Microsoft.eShopWeb;

public class PayPalOptions
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override; when set, used verbatim as the PayPal API base address for every call.</summary>
    public string? BaseUrl { get; set; }
}
