using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints.Models;

/// <summary>
/// Raw card details sent by the shopper. Never persisted or logged by the application; forwarded
/// straight to PayPal for a one-off charge or to be vaulted.
/// </summary>
public class CardModel
{
    /// <summary>Primary account number, e.g. 4111111111111111 (sandbox test Visa).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, e.g. 2030-01. Any future date works in sandbox.</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security / CVC code. Any value works in sandbox.</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Optional cardholder name.</summary>
    public string? CardholderName { get; set; }

    /// <summary>Optional billing address.</summary>
    public BillingAddressModel? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new CardDetails
    {
        Number = Number?.Replace(" ", string.Empty) ?? string.Empty,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddress = BillingAddress?.ToBillingAddressDetails()
    };
}
