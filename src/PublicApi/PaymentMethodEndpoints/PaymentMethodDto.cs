using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.Id,
            LastDigits = method.LastDigits,
            Brand = method.Brand,
            Expiry = method.Expiry ?? string.Empty,
            CardholderName = method.CardholderName
        };
}
