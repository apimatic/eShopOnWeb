using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Raw card details supplied by the caller. Used to pay directly or to save a card. Never stored or logged.</summary>
public class CardRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public BillingAddressRequest BillingAddress { get; set; } = new();

    public CardDetails ToCardDetails() => new(
        CardNumber,
        ExpiryMonth,
        ExpiryYear,
        SecurityCode,
        CardholderName,
        BillingAddress.ToBillingAddress());
}

public class BillingAddressRequest
{
    public string Line1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;

    public BillingAddress ToBillingAddress() => new(Line1, City, State, PostalCode, CountryCode);
}
