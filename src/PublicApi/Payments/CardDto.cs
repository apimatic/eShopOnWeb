using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Card details as they arrive over the wire, for a one-off payment or to be vaulted. These are
/// passed straight through to PayPal and never persisted or logged by this app.
/// </summary>
public class CardDto
{
    /// <summary>Primary account number (digits only), e.g. "4111111111111111".</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in "YYYY-MM" format, e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card verification value (CVV/CVC).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    public string? CardholderName { get; set; }

    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddress = BillingAddress?.ToCardBillingAddress()
    };
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Two-letter ISO country code, e.g. "US".</summary>
    public string CountryCode { get; set; } = string.Empty;

    public CardBillingAddress ToCardBillingAddress() => new()
    {
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        State = State,
        PostalCode = PostalCode,
        CountryCode = CountryCode
    };
}
