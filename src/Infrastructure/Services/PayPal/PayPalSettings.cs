namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');
        return Environment == "live"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
