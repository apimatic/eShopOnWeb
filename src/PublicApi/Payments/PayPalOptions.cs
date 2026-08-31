using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";
    [Required] public string ClientId { get; set; } = string.Empty;
    [Required] public string ClientSecret { get; set; } = string.Empty;
    [Required] public string Environment { get; set; } = string.Empty;
    [Required, RegularExpression("^[A-Z]{3}$")] public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri ApiBase => !string.IsNullOrWhiteSpace(BaseUrl)
        ? new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute)
        : Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
            ? new Uri("https://api-m.paypal.com/")
            : Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                ? new Uri("https://api-m.sandbox.paypal.com/")
                : throw new InvalidOperationException("PayPal:Environment must be 'sandbox' or 'live'.");
}
