using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByIdAndBuyerSpec : Specification<SavedCard>
{
    public SavedCardByIdAndBuyerSpec(int cardId, string buyerId)
    {
        Query.Where(c => c.Id == cardId && c.BuyerId == buyerId);
    }
}
