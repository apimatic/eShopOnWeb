namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by a caller for a one-off payment or to save a card. These are handled
/// in-flight only — passed straight to PayPal and never persisted or logged by this application.
/// </summary>
public class CardInput
{
    /// <summary>Name on the card.</summary>
    public string CardholderName { get; set; } = string.Empty;

    /// <summary>Card number (PAN). For the sandbox test Visa use 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry month, 1-12.</summary>
    public int ExpiryMonth { get; set; }

    /// <summary>Expiry year, four digits (any future date for the sandbox test card).</summary>
    public int ExpiryYear { get; set; }

    /// <summary>Card security code (CVC).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Optional billing address.</summary>
    public BillingAddressInput? BillingAddress { get; set; }
}
