namespace Microsoft.eShopWeb.PublicApi.Payments;
public sealed class PayPalOptions { public string ClientId { get; set; } = ""; public string ClientSecret { get; set; } = ""; public string Environment { get; set; } = "sandbox"; public string Currency { get; set; } = "USD"; public string? BaseUrl { get; set; } }
