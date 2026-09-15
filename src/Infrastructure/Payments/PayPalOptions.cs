namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        var environment = Environment?.Trim();
        if (string.Equals(environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "production", System.StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }
}
