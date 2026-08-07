using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Card details supplied by the caller for a one-off payment or to save a card. These are forwarded
/// to PayPal and never persisted or logged by this application.
/// </summary>
public class CardDto
{
    /// <summary>Primary account number (13–19 digits). Spaces/dashes are tolerated.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM format, e.g. 2030-01.</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security code (CVV), 3–4 digits.</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name as printed on the card.</summary>
    public string? CardholderName { get; set; }

    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToDomain() => new(
        Number,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress?.ToDomain());
}

/// <summary>Billing address for a card, mapped to PayPal's address model.</summary>
public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>City / town.</summary>
    public string? AdminArea2 { get; set; }
    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Two-letter country code, e.g. US.</summary>
    public string? CountryCode { get; set; }

    public BillingAddressDetails ToDomain() => new(
        AddressLine1,
        AddressLine2,
        AdminArea2,
        AdminArea1,
        PostalCode,
        CountryCode);
}
