using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(b => b.IdentityGuid == buyerId)
            .Include(b => b.PaymentMethods);
    }
}
