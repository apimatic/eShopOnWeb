using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>"sandbox" or "live".</summary>
    public string Environment { get; set; } = "sandbox";
    /// <summary>Currency all payments are processed in, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";
    /// <summary>Optional verbatim override of the API base address, used for every PayPal call.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
