using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Card details as accepted on the wire for a one-off payment or to save a card. These are passed
/// straight through to PayPal and never persisted or logged by this application.
/// </summary>
public class CardModel
{
    /// <summary>The primary account number (e.g. the sandbox test card 4111111111111111).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as "YYYY-MM" (e.g. "2027-05").</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>The card security code (CVV).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>The cardholder name as it appears on the card.</summary>
    public string? CardholderName { get; set; }

    public BillingAddressModel? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress?.ToPayPalAddress());
}

/// <summary>A billing address in PayPal's portable-address shape.</summary>
public class BillingAddressModel
{
    /// <summary>2-character ISO 3166-1 country code (e.g. "US").</summary>
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

    /// <summary>City / town.</summary>
    public string? AdminArea2 { get; set; }

    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }

    public PayPalBillingAddress ToPayPalAddress() => new(
        CountryCode,
        AddressLine1,
        AddressLine2,
        AdminArea2,
        AdminArea1,
        PostalCode);
}
