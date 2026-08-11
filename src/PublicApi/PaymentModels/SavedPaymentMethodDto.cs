using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A saved card, described safely enough to recognise it — never full card details.</summary>
public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;

    /// <summary>A masked display label, e.g. "VISA •••• 1111".</summary>
    public string Display { get; set; } = string.Empty;

    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedPaymentMethodDto FromEntity(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        Brand = m.Brand,
        LastDigits = m.LastDigits,
        Display = $"{m.Brand} •••• {m.LastDigits}",
        Expiry = m.Expiry,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt
    };
}
