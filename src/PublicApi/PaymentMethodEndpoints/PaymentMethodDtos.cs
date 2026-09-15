using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Safe description of a saved card — enough to recognise it, never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }

    public static PaymentMethodDto From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Alias = method.Alias,
        CardBrand = method.CardBrand,
        Last4 = method.Last4,
        Expiry = method.Expiry
    };
}
