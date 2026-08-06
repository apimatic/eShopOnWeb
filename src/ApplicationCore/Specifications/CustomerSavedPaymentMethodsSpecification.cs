using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All saved cards belonging to one shopper, newest first.</summary>
public class CustomerSavedPaymentMethodsSpecification : Specification<SavedPaymentMethod>
{
    public CustomerSavedPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId)
            .OrderByDescending(pm => pm.CreatedDate);
    }
}
