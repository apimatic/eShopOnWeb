using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalSettings
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    [Required]
    public string Environment { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z]{3}$")]
    public string Currency { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }
}
