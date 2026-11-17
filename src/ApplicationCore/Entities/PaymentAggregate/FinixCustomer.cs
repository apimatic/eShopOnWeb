using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class FinixCustomer : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private FinixCustomer() { }
#pragma warning restore CS8618

    public FinixCustomer(string ownerId, string finixIdentityId)
    {
        OwnerId = ownerId;
        FinixIdentityId = finixIdentityId;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string FinixIdentityId { get; private set; } = string.Empty;
}
