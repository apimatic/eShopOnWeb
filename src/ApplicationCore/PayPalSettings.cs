using System;

namespace Microsoft.eShopWeb;

public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var overrideUrl = BaseUrl.Trim();
            return overrideUrl.EndsWith('/') ? overrideUrl : overrideUrl + "/";
        }

        var isLive = string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase);

        return isLive
            ? "https://api-m.paypal.com/"
            : "https://api-m.sandbox.paypal.com/";
    }
}
