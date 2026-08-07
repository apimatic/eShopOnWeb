using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Raw card details supplied by the caller for a one-off payment or to be saved. These are passed
/// straight to PayPal and are never persisted in this application's database or written to logs.
/// </summary>
public class CardDto
{
    /// <summary>Primary account number, e.g. the sandbox test Visa 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as YYYY-MM, e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string CardholderName { get; set; } = string.Empty;

    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number: (Number ?? string.Empty).Replace(" ", string.Empty).Trim(),
        ExpiryMonthYear: (Expiry ?? string.Empty).Trim(),
        SecurityCode: (SecurityCode ?? string.Empty).Trim(),
        CardholderName: (CardholderName ?? string.Empty).Trim(),
        BillingAddress: BillingAddress?.ToCardBillingAddress());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardBillingAddress ToCardBillingAddress()
        => new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
