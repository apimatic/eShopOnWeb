using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Raw card details as supplied by a caller. Never logged, never persisted as-is.</summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = "";

    /// <summary>"YYYY-MM"</summary>
    public string ExpiryYearMonth { get; set; } = "";
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        ExpiryYearMonth,
        SecurityCode,
        CardholderName,
        BillingAddress?.ToCardBillingAddress());
}

public class CardBillingAddressRequest
{
    public string CountryCode { get; set; } = "";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    public CardBillingAddress ToCardBillingAddress() => new(
        CountryCode, AddressLine1, AddressLine2, City, State, PostalCode);
}
