using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class ShopperPaymentProfile : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ShopperPaymentProfile() {}

    public ShopperPaymentProfile(string buyerId, string finixIdentityId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));

        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
    }

    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
}
