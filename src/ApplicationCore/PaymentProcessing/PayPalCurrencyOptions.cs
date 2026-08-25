namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>The only part of PayPal configuration ApplicationCore needs directly: the currency to charge orders in. Bound from the "PayPal" configuration section; every other PayPal setting (credentials, environment, base URL) is Infrastructure's concern.</summary>
public class PayPalCurrencyOptions
{
    public string Currency { get; set; } = "USD";
}
