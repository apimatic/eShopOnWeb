using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new();
}

/// <summary>Identifies the saved card and describes it safely — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }

    public static PaymentMethodDto FromEntity(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        LastDigits = card.LastDigits,
        Brand = card.Brand,
        Expiry = card.Expiry
    };
}
