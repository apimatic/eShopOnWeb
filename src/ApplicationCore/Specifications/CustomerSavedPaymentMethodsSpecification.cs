using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's own saved cards.</summary>
public class CustomerSavedPaymentMethodsSpecification : Specification<SavedPaymentMethod>
{
    public CustomerSavedPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}
