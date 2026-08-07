using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A saved card, described safely enough for a shopper to recognise it — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? CardholderName { get; set; }

    /// <summary>Friendly one-line description, e.g. "VISA ending in 1111 (exp 12/2030)".</summary>
    public string Description { get; set; } = string.Empty;

    public static PaymentMethodDto FromEntity(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        Last4 = pm.Last4,
        ExpiryMonth = pm.ExpiryMonth,
        ExpiryYear = pm.ExpiryYear,
        CardholderName = pm.Alias,
        Description = pm.Description
    };
}
