using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByBuyerSpec : Specification<SavedCard>
{
    public SavedCardsByBuyerSpec(string buyerId, bool includeRemoved = false)
    {
        Query.Where(c => c.BuyerId == buyerId);

        if (!includeRemoved)
        {
            Query.Where(c => !c.IsRemoved);
        }

        Query.OrderByDescending(c => c.CreatedAt);
    }
}

public class SavedCardByIdSpec : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(c => c.Id == paymentMethodId && c.BuyerId == buyerId && !c.IsRemoved);
    }
}
