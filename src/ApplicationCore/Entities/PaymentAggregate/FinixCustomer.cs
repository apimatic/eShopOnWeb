using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an authenticated shopper to the Finix buyer identity used to tokenize their cards.
/// </summary>
public class FinixCustomer : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private FinixCustomer() { }

    public FinixCustomer(string ownerId, string identityId)
    {
        OwnerId = ownerId;
        IdentityId = identityId;
    }

    public string OwnerId { get; private set; }
    public string IdentityId { get; private set; }
}
