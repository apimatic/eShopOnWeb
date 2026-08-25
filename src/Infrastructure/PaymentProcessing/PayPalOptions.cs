namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

// Bound from the "PayPal" configuration section. Values come from environment variables loaded
// into user-secrets locally (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_ENVIRONMENT,
// PAYPAL_CURRENCY) - never hard-coded here.
public class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // "sandbox" or "live".
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    // Optional override: when set, used verbatim as the API base address for every PayPal call,
    // including the OAuth2 token request, instead of deriving one from Environment.
    public string? BaseUrl { get; set; }
}
