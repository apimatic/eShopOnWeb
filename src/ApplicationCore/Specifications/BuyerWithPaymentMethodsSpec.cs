using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads a buyer (by their identity) together with their saved cards.</summary>
public class BuyerWithPaymentMethodsSpec : Specification<Buyer>
{
    public BuyerWithPaymentMethodsSpec(string identityGuid)
    {
        Query
            .Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }
}
