using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

internal static class PaymentMethodMapping
{
    public static PaymentMethodDto ToDto(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Brand = paymentMethod.Brand,
        Last4 = paymentMethod.Last4,
        ExpiryYearMonth = paymentMethod.ExpiryYearMonth,
        Alias = paymentMethod.Alias,
        Description = paymentMethod.Describe()
    };
}
