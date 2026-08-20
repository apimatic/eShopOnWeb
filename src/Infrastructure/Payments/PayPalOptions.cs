namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "sandbox", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";
    }
}
