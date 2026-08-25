using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private PaymentMethod() { }

    public PaymentMethod(
        string buyerId,
        string payPalTokenId,
        string? payPalCustomerId,
        string? cardLastFour,
        string? cardBrand,
        string? cardExpiry)
    {
        BuyerId = buyerId;
        PayPalTokenId = payPalTokenId;
        PayPalCustomerId = payPalCustomerId;
        CardLastFour = cardLastFour;
        CardBrand = cardBrand;
        CardExpiry = cardExpiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? CardLastFour { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardExpiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
