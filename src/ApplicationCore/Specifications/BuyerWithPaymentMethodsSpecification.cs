using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a buyer with their saved cards, keyed by the identity taken from the caller's token, so
/// saved-card operations are always scoped to the signed-in shopper.
/// </summary>
public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>, ISingleResultSpecification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identity)
    {
        // PaymentMethods is an owned collection and is loaded automatically by EF Core, so no Include.
        Query.Where(b => b.IdentityGuid == identity);
    }
}
