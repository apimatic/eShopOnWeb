namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "Sandbox";
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
