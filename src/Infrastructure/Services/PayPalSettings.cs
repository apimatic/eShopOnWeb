namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string ResolvedBaseUrl =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl.TrimEnd('/')
            : Environment.Equals("live", System.StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
}
