using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Raw card details supplied by a shopper. Never stored in this app's database and never logged.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string CardholderName { get; set; } = string.Empty;

    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToDomain()
    {
        var billing = BillingAddress ?? new BillingAddressDto();
        return new CardDetails(
            Number,
            Expiry,
            SecurityCode,
            CardholderName,
            new BillingAddress(
                billing.AddressLine1,
                billing.AddressLine2,
                billing.City,
                billing.State,
                billing.PostalCode,
                string.IsNullOrWhiteSpace(billing.CountryCode) ? "US" : billing.CountryCode));
    }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
