using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int id)
    {
        Query.Where(m => m.Id == id);
    }
}
