using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A saved card by its id, scoped to its owning shopper so one shopper can never act on another's.</summary>
public class SavedCardByIdSpec : Specification<SavedCard>
{
    public SavedCardByIdSpec(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}
