using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodByTokenSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByTokenSpec(string paymentTokenId)
    {
        Query.Where(m => m.PaymentTokenId == paymentTokenId);
    }
}
