using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Raw card details supplied by a caller for a one-off payment or to save a card. Never persisted or logged.</summary>
public class CardModel
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in "YYYY-MM" form (PayPal format).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressModel? BillingAddress { get; set; }

    public PayPalCardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress?.AddressLine1,
        BillingAddress?.AddressLine2,
        BillingAddress?.City,
        BillingAddress?.State,
        BillingAddress?.PostalCode,
        BillingAddress?.CountryCode);
}

public class BillingAddressModel
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
