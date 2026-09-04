using Microsoft.eShopWeb.ApplicationCore.Integrations.PayPal;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// One-off card details supplied on a pay/save request. Transient by design:
/// this DTO is mapped straight onto the provider call and never persisted.
/// </summary>
public class CardRequestDto
{
    public string? Number { get; init; }

    /// <summary>MM/YY or YYYY-MM.</summary>
    public string? Expiry { get; init; }

    public string? Cvv { get; init; }
    public string? CardholderName { get; init; }
    public CardBillingAddressDto? BillingAddress { get; init; }

    public CardDetails ToCardDetails() => new CardDetails
    {
        Number = Number ?? string.Empty,
        Expiry = Expiry ?? string.Empty,
        Cvv = Cvv,
        CardHolderName = CardholderName,
        BillingAddress = BillingAddress is null ? null : new CardBillingAddress
        {
            Street = BillingAddress.Street,
            City = BillingAddress.City,
            State = BillingAddress.State,
            PostalCode = BillingAddress.PostalCode,
            CountryCode = BillingAddress.CountryCode
        }
    };
}

public class CardBillingAddressDto
{
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}
