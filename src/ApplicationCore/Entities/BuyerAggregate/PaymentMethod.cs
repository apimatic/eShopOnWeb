using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string? Alias { get; private set; }
    public string CardId { get; private set; }
    public string Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }

#pragma warning disable CS8618
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string cardId, string last4, string? brand, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        Alias = alias;
    }
}
