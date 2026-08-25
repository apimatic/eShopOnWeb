using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerWithPaymentMethodsSpec : Specification<Buyer>
{
    public BuyerWithPaymentMethodsSpec(string identityGuid)
    {
        Query
            .Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }

    public BuyerWithPaymentMethodsSpec(int buyerId)
    {
        Query
            .Where(b => b.Id == buyerId)
            .Include(b => b.PaymentMethods);
    }
}
