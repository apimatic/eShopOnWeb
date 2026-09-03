namespace Microsoft.eShopWeb.PublicApi.Payments;
public sealed class PayPalOptions
{
 public string ClientId { get; set; } = ""; public string ClientSecret { get; set; } = "";
 public string Environment { get; set; } = "sandbox"; public string Currency { get; set; } = "USD"; public string? BaseUrl { get; set; }
 public string ApiBase => !string.IsNullOrWhiteSpace(BaseUrl) ? BaseUrl!.TrimEnd('/') : (Environment.Equals("live", System.StringComparison.OrdinalIgnoreCase) ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com");
}
