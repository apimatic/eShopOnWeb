using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Raw card details supplied by a caller. Passed straight through to PayPal; never
/// persisted or logged by this API.
/// </summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public bool HasCoreDetails =>
        !string.IsNullOrWhiteSpace(Number)
        && !string.IsNullOrWhiteSpace(Expiry)
        && !string.IsNullOrWhiteSpace(SecurityCode);

    public CardPaymentDetails ToCardPaymentDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        AddressLine1 = AddressLine1 ?? string.Empty,
        AddressLine2 = AddressLine2,
        City = City ?? string.Empty,
        State = State ?? string.Empty,
        PostalCode = PostalCode ?? string.Empty,
        CountryCode = string.IsNullOrWhiteSpace(CountryCode) ? "US" : CountryCode!
    };
}
