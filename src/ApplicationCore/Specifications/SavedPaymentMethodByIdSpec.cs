using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId)
    {
        Query.Where(p => p.Id == paymentMethodId && !p.IsDeleted);
    }
}
