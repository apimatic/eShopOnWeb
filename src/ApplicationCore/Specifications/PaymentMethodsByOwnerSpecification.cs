using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All saved cards belonging to a single shopper, newest first.</summary>
public class PaymentMethodsByOwnerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByOwnerSpecification(string ownerId)
    {
        Query
            .Where(pm => pm.OwnerId == ownerId)
            .OrderByDescending(pm => pm.CreatedDate);
    }
}
