using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Card details supplied by the shopper for a one-off payment or to vault. These are forwarded
/// straight to PayPal and are never stored in this application's database or written to logs.
/// </summary>
public class CardRequest
{
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Card expiry as YYYY-MM (e.g. 2028-04).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string? CardholderName { get; set; }

    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        CardNumber?.Replace(" ", string.Empty) ?? string.Empty,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress?.ToCardBillingAddress());
}

/// <summary>Optional billing address for a card.</summary>
public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardBillingAddress ToCardBillingAddress() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode ?? "US");
}
