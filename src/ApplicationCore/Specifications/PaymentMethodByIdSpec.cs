using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodByIdSpec : Specification<PaymentMethod>
{
    public PaymentMethodByIdSpec(int id)
    {
        Query.Where(pm => pm.Id == id);
    }
}
