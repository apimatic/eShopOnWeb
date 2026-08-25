using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string GetBaseUrl() =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl.TrimEnd('/')
            : Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
}
