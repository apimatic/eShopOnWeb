using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Raw card details as submitted by a caller - never persisted, forwarded to PayPal and discarded.</summary>
public class CardDetailsDto
{
    public string Number { get; set; } = default!;
    /// <summary>"YYYY-MM".</summary>
    public string Expiry { get; set; } = default!;
    public string SecurityCode { get; set; } = default!;
    public string CardholderName { get; set; } = default!;
    public string AddressLine1 { get; set; } = default!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = default!;
    public string? State { get; set; }
    public string PostalCode { get; set; } = default!;
    public string CountryCode { get; set; } = default!;

    public PayPalCardDetails ToPayPalCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        State = State,
        PostalCode = PostalCode,
        CountryCode = CountryCode
    };
}
