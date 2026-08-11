using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByIdForBuyerSpec : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdForBuyerSpec(string buyerId, int paymentMethodId)
    {
        // Scoped to the owner: another shopper's card id simply does not match.
        Query.Where(c => c.Id == paymentMethodId && c.BuyerId == buyerId);
    }
}
