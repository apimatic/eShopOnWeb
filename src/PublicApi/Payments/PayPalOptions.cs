using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required] public string ClientId { get; init; } = string.Empty;
    [Required] public string ClientSecret { get; init; } = string.Empty;
    [Required] public string Environment { get; init; } = string.Empty;
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
}
