using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }

    public static PaymentMethodDto FromSavedPaymentMethod(SavedPaymentMethod method) => new PaymentMethodDto
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}
