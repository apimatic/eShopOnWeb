namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";
    public string ClientId { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
    public string Environment { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return BaseUrl;
        return Environment.Equals("Live", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
