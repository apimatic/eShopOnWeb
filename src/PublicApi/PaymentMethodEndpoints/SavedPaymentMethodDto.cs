using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A saved card described safely enough to recognise it — never full card details.</summary>
public class SavedPaymentMethodDto
{
    public int Id { get; set; }
    public string? Brand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static SavedPaymentMethodDto From(SavedPaymentMethod method) => new()
    {
        Id = method.Id,
        Brand = method.Brand,
        LastFourDigits = method.LastFourDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedDate = method.CreatedDate
    };
}
