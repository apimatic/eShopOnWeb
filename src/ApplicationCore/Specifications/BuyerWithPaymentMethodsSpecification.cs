using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads a buyer (by their identity/username) together with their saved cards.</summary>
public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identity)
    {
        Query
            .Where(b => b.IdentityGuid == identity)
            .Include(b => b.PaymentMethods);
    }
}
