using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied on a request. Carried through to PayPal only; never persisted in the
/// application database and never logged.
/// </summary>
public class CardRequest
{
    /// <summary>Primary account number.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in "YYYY-MM" form, e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security code (CVV/CVC).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name as it appears on the card.</summary>
    public string CardholderName { get; set; } = string.Empty;

    /// <summary>Billing address (country is required by PayPal for card processing).</summary>
    public BillingAddressRequest? BillingAddress { get; set; }

    public bool IsPopulated() =>
        !string.IsNullOrWhiteSpace(Number) || !string.IsNullOrWhiteSpace(SecurityCode) ||
        !string.IsNullOrWhiteSpace(Expiry) || !string.IsNullOrWhiteSpace(CardholderName);

    public CardDetails ToCardDetails()
    {
        var billing = BillingAddress ?? new BillingAddressRequest();
        return new CardDetails(
            Number.Trim(),
            Expiry.Trim(),
            SecurityCode.Trim(),
            CardholderName.Trim(),
            new BillingAddress(
                billing.AddressLine1,
                billing.AddressLine2,
                billing.City,
                billing.State,
                billing.PostalCode,
                string.IsNullOrWhiteSpace(billing.CountryCode) ? "US" : billing.CountryCode!.Trim()));
    }
}

/// <summary>Billing address for a card.</summary>
public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "US" if omitted.</summary>
    public string? CountryCode { get; set; }
}
