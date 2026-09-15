using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    [Required]
    public string Environment { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/", UriKind.Absolute);
        }

        if (Environment.Equals("sandbox", System.StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("https://api-m.sandbox.paypal.com/");
        }

        throw new ValidationException("PayPal:Environment must be 'sandbox' unless PayPal:BaseUrl is explicitly configured.");
    }
}
