using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ActiveSavedCardsByBuyerSpec : Specification<SavedCard>
{
    public ActiveSavedCardsByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId && !c.IsDeleted);
    }
}

public class SavedCardByIdAndBuyerSpec : Specification<SavedCard>
{
    public SavedCardByIdAndBuyerSpec(int cardId, string buyerId)
    {
        Query.Where(c => c.Id == cardId && c.BuyerId == buyerId);
    }
}
