using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identityGuid)
    {
        Query.Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }

    public BuyerWithPaymentMethodsSpecification(int buyerId)
    {
        Query.Where(b => b.Id == buyerId)
            .Include(b => b.PaymentMethods);
    }
}
