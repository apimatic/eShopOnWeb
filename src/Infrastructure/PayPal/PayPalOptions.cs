using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalOptions : IPayPalSettings
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
            return BaseUrl.TrimEnd('/');
        }

        var env = Environment?.Trim();
        if (string.Equals(env, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "production", System.StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }
}
