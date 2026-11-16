using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public sealed class PaymentAddressDto
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public PaymentAddress ToPaymentAddress() => new()
    {
        Line1 = Line1,
        Line2 = Line2,
        City = City,
        Region = Region,
        PostalCode = PostalCode,
        Country = Country
    };
}

public sealed class PaymentCardDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public PaymentAddressDto? Address { get; set; }

    public PaymentCardDetails ToPaymentCardDetails() => new()
    {
        Number = Number,
        ExpirationMonth = ExpirationMonth,
        ExpirationYear = ExpirationYear,
        SecurityCode = SecurityCode,
        Name = Name,
        Address = Address?.ToPaymentAddress()
    };
}
