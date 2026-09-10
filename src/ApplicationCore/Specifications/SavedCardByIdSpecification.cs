using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByIdSpecification : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdSpecification(int paymentMethodId)
    {
        Query.Where(c => c.Id == paymentMethodId);
    }
}
