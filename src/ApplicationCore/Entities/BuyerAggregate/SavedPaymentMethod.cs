using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    public string PayPalPaymentTokenId { get; private set; }

    public string? CardLastFourDigits { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardholderName { get; private set; }
    public string? CardExpiryDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.Now;

    #pragma warning disable CS8618
    private SavedPaymentMethod() {}

    public SavedPaymentMethod(string buyerId, string payPalPaymentTokenId, string? cardLastFourDigits,
        string? cardBrand, string? cardholderName, string? cardExpiryDate)
    {
        BuyerId = buyerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        CardLastFourDigits = cardLastFourDigits;
        CardBrand = cardBrand;
        CardholderName = cardholderName;
        CardExpiryDate = cardExpiryDate;
    }
}
