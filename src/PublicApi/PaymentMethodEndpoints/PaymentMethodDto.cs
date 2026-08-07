using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A saved card described safely — enough to recognise it, never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto FromEntity(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        Label = pm.Label,
        CreatedDate = pm.CreatedDate
    };
}
