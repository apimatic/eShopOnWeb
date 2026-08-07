using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a buyer (by their identity / username from the token) together with their saved cards.
/// Scoping every payment-method operation through this spec is what guarantees one shopper can never
/// see, use, or delete another shopper's saved card.
/// </summary>
public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>, ISingleResultSpecification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identityGuid)
    {
        Query
            .Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }
}
