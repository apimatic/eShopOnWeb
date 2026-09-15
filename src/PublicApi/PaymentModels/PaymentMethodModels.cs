using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A saved card described safely enough for the shopper to recognise it — never full details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public string? Alias { get; set; }

    public static PaymentMethodDto FromEntity(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        CardholderName = pm.CardholderName,
        Alias = pm.Alias,
    };
}
