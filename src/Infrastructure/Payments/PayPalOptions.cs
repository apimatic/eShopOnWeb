namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
}
