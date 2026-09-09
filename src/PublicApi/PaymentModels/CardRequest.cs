using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied for a one-off payment or to save a card. These are forwarded straight to
/// PayPal and never persisted or logged by this app.
/// </summary>
public class CardRequest
{
    /// <summary>Card number, e.g. the sandbox test card 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, any future date for the sandbox card.</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security code (CVC).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name.</summary>
    public string? Name { get; set; }

    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        Name,
        BillingAddress?.ToBillingAddress());
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public BillingAddress ToBillingAddress() => new(AddressLine1, City, State, PostalCode, CountryCode);
}
