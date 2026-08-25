using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByTokenIdSpec : Specification<SavedCard>
{
    public SavedCardByTokenIdSpec(string paymentMethodId)
    {
        Query.Where(c => c.PayPalPaymentTokenId == paymentMethodId && !c.IsDeleted);
    }
}
