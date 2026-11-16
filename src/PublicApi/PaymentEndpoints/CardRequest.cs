using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CardAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public CardAddressInput ToInput() => new()
    {
        Line1 = Line1,
        Line2 = Line2,
        City = City,
        Region = Region,
        PostalCode = PostalCode,
        Country = Country
    };
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardAddressRequest? Address { get; set; }

    public CardInput ToInput() => new()
    {
        Number = Number,
        ExpirationMonth = ExpirationMonth,
        ExpirationYear = ExpirationYear,
        SecurityCode = SecurityCode,
        Name = Name,
        Address = Address?.ToInput()
    };
}
