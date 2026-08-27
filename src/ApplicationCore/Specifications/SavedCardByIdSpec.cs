using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByIdSpec : Specification<SavedCard>
{
    public SavedCardByIdSpec(int savedCardId)
    {
        Query.Where(c => c.Id == savedCardId);
    }
}
