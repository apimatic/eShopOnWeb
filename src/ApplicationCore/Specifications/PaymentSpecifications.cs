using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByOwnerSpecification : Specification<SavedCard>
{
    public SavedCardsByOwnerSpecification(string ownerId)
    {
        Query.Where(card => card.OwnerId == ownerId && !card.IsDeleted)
            .OrderByDescending(card => card.CreatedAt);
    }
}

public class SavedCardByIdAndOwnerSpecification : Specification<SavedCard>
{
    public SavedCardByIdAndOwnerSpecification(int cardId, string ownerId)
    {
        Query.Where(card => card.Id == cardId && card.OwnerId == ownerId && !card.IsDeleted);
    }
}

public class FinixCustomerByOwnerSpecification : Specification<FinixCustomer>
{
    public FinixCustomerByOwnerSpecification(string ownerId)
    {
        Query.Where(customer => customer.OwnerId == ownerId);
    }
}
