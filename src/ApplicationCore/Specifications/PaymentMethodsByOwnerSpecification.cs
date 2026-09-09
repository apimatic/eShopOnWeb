using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's saved cards, newest first.</summary>
public class PaymentMethodsByOwnerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByOwnerSpecification(string ownerId)
    {
        Query.Where(pm => pm.OwnerId == ownerId)
            .OrderByDescending(pm => pm.CreatedAt);
    }
}
