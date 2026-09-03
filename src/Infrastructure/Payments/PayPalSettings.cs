using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalSettings
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; init; } = string.Empty;

    [Required]
    public string ClientSecret { get; init; } = string.Empty;

    [Required]
    public string Environment { get; init; } = string.Empty;

    [Required, RegularExpression("^[A-Z]{3}$")]
    public string Currency { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }
}
