using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>, ISingleResultSpecification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identity)
    {
        Query
            .Where(buyer => buyer.IdentityGuid == identity)
            .Include(buyer => buyer.PaymentMethods);
    }
}
