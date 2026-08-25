using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string paymentTokenId, string? last4, string? brand, string? expiry, string? cardholderName)
    {
        BuyerId = buyerId;
        PaymentTokenId = paymentTokenId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string PaymentTokenId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public bool IsDeleted { get; private set; }

    public void Delete() => IsDeleted = true;
}
