using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

// Raw card details for a one-off payment or to save a new card. Never persisted or logged by this
// application - forwarded straight to PayPal and discarded.
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public AddressDto BillingAddress { get; set; } = new();
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;

    // ISO 3166-1 alpha-2 country code (e.g. "US") - PayPal requires the 2-letter code, not a full name.
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public static class CardDetailsMappingExtensions
{
    public static CardDetails ToCardDetails(this CardDetailsDto dto) => new(
        dto.Number,
        dto.ExpiryMonth,
        dto.ExpiryYear,
        dto.SecurityCode,
        dto.CardholderName,
        new Address(dto.BillingAddress.Street, dto.BillingAddress.City, dto.BillingAddress.State, dto.BillingAddress.Country, dto.BillingAddress.ZipCode));
}
