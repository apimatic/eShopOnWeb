using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    public string? Environment { get; set; }

    [Required]
    public string Currency { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }
}
