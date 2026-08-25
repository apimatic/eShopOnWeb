namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

// The subset of payment-provider configuration ApplicationCore itself needs. Bound from the same
// "PayPal" configuration section as the Infrastructure-layer gateway settings; unrelated keys
// (ClientId, ClientSecret, ...) are simply ignored by the binder.
public class PaymentSettings
{
    public string Currency { get; set; } = "USD";
}
