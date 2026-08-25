using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string LastDigits { get; set; } = default!;
    public string Expiry { get; set; } = default!;
    public string? CardholderName { get; set; }

    public static PaymentMethodDto FromPaymentMethod(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Brand = paymentMethod.Brand,
        LastDigits = paymentMethod.LastDigits,
        Expiry = paymentMethod.Expiry,
        CardholderName = paymentMethod.CardholderName
    };
}
