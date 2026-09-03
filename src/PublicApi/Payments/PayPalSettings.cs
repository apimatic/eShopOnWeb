namespace Microsoft.eShopWeb.PublicApi.Payments;
public sealed class PayPalSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "";
    public string Currency { get; set; } = "";
    public string? BaseUrl { get; set; }
}
