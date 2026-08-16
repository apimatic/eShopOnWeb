using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Card details supplied by a caller for a one-off payment or to be saved. These are passed
/// straight through to PayPal and are never persisted or logged by this application.
/// </summary>
public class CardInfo
{
    /// <summary>Primary account number, e.g. the sandbox test card 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry month, 1-12.</summary>
    public int ExpiryMonth { get; set; }

    /// <summary>Expiry year, four digits (a two-digit year is interpreted as 20xx).</summary>
    public int ExpiryYear { get; set; }

    public string SecurityCode { get; set; } = string.Empty;

    public string? Name { get; set; }

    public BillingAddressInfo? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        var year = ExpiryYear < 100 ? 2000 + ExpiryYear : ExpiryYear;
        var expiry = $"{year:D4}-{ExpiryMonth:D2}"; // PayPal expects "YYYY-MM"

        return new CardDetails(
            Number: Number,
            Expiry: expiry,
            SecurityCode: SecurityCode,
            Name: Name,
            BillingAddress: BillingAddress?.ToBillingAddress());
    }
}

public class BillingAddressInfo
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public BillingAddress ToBillingAddress() => new(
        AddressLine1: AddressLine1,
        AddressLine2: AddressLine2,
        AdminArea2: City,
        AdminArea1: State,
        PostalCode: PostalCode,
        CountryCode: CountryCode);
}
