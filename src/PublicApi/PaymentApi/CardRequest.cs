using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentApi;

/// <summary>
/// Raw card details supplied by the shopper for a one-off payment or to save a card. These are
/// forwarded straight to PayPal and are never stored in this application's database or logs.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry, accepted as YYYY-MM (also tolerant of MM/YY and MM/YYYY).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string? CardholderName { get; set; }

    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        var billing = BillingAddress is null
            ? null
            : new CardBillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.AdminArea2,
                BillingAddress.AdminArea1,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode);

        return new CardDetails(Number, Expiry, SecurityCode, CardholderName, billing);
    }
}

/// <summary>A billing address for card AVS. PayPal field names are used to avoid ambiguity.</summary>
public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

    /// <summary>City / town.</summary>
    public string? AdminArea2 { get; set; }

    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }

    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
