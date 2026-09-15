using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CustomerSavedPaymentMethodsSpecification : Specification<SavedPaymentMethod>
{
    public CustomerSavedPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}
