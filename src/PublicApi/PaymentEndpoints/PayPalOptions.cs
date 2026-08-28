using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required] public string ClientId { get; set; } = string.Empty;
    [Required] public string ClientSecret { get; set; } = string.Empty;
    [Required] public string Environment { get; set; } = string.Empty;
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
