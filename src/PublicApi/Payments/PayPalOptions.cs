namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return BaseUrl;

        return Environment.ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException("PayPal:Environment must be 'sandbox' or 'live'.")
        };
    }
}
