namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string Environment { get; init; } = "Sandbox";
    public string Currency { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
}
