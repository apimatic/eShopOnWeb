namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixOptions
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string BaseUrl { get; set; } = string.Empty;
}
