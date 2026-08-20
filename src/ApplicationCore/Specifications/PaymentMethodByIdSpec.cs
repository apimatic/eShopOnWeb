using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodByIdSpec : Specification<PaymentMethod>, ISingleResultSpecification<PaymentMethod>
{
    public PaymentMethodByIdSpec(int paymentMethodId)
    {
        Query.Where(paymentMethod => paymentMethod.Id == paymentMethodId);
    }
}
