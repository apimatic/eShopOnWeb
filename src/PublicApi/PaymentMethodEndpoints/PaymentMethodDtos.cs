using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class PaymentMethodDtoMapper
{
    public static PaymentMethodDto ToDto(PaymentMethod paymentMethod) =>
        new()
        {
            PaymentMethodId = paymentMethod.Id,
            Brand = paymentMethod.Brand,
            Last4 = paymentMethod.Last4,
            ExpirationMonth = paymentMethod.ExpirationMonth,
            ExpirationYear = paymentMethod.ExpirationYear,
            CardholderName = paymentMethod.CardholderName,
            Alias = paymentMethod.Alias,
            CreatedAt = paymentMethod.CreatedAt
        };
}
