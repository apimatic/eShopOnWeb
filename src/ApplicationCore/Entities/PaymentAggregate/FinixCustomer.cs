using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class FinixCustomer : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private FinixCustomer() { }
    #pragma warning restore CS8618

    public FinixCustomer(string buyerId, string finixIdentityId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));

        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
    }

    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
}
