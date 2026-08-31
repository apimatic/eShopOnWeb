using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Safe description of a saved card: enough for the shopper to recognise which card
/// it is, never full card details.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto FromSavedPaymentMethod(SavedPaymentMethod saved)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastDigits = saved.LastDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName,
            CreatedAt = saved.CreatedAt
        };
    }
}
