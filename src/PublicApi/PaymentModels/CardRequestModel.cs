using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by the caller for a one-off payment or to save a card. These values are
/// forwarded to PayPal only; they are never persisted in this application's database nor logged.
/// </summary>
public class CardRequestModel
{
    /// <summary>Primary account number (13–19 digits), e.g. the sandbox test card 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in ISO-8601 <c>YYYY-MM</c> form, e.g. <c>2030-01</c>.</summary>
    public string ExpiryMonthYear { get; set; } = string.Empty;

    /// <summary>Card security code / CVV (3–4 digits).</summary>
    public string? SecurityCode { get; set; }

    /// <summary>Cardholder name as it appears on the card.</summary>
    public string? CardholderName { get; set; }

    public BillingAddressModel? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        ExpiryMonthYear = ExpiryMonthYear,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddress = BillingAddress?.ToBillingAddress()
    };
}

/// <summary>Billing address for a card. Field names mirror the PayPal card billing address object.</summary>
public class BillingAddressModel
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

    /// <summary>City / town (PayPal <c>admin_area_2</c>).</summary>
    public string? AdminArea2 { get; set; }

    /// <summary>State / province (PayPal <c>admin_area_1</c>).</summary>
    public string? AdminArea1 { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>Two-letter ISO-3166-1 country code, e.g. <c>US</c>.</summary>
    public string CountryCode { get; set; } = string.Empty;

    public CardBillingAddress ToBillingAddress() => new()
    {
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        AdminArea2 = AdminArea2,
        AdminArea1 = AdminArea1,
        PostalCode = PostalCode,
        CountryCode = CountryCode
    };
}
