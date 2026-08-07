namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied by a shopper for a one-off payment or to be saved.
/// These are passed straight through to PayPal and are never persisted or logged
/// by this application.
/// </summary>
public class CardPaymentDetails
{
    /// <summary>Primary account number (PAN).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM (PayPal's required format).</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security code (CVC/CVV).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name as it appears on the card.</summary>
    public string CardholderName { get; set; } = string.Empty;

    // Billing address (PayPal accepts any valid address for the sandbox test card).
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
}
