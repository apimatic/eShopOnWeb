using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All saved cards belonging to a single shopper.</summary>
public class CustomerPaymentMethodsSpecification : Specification<CustomerPaymentMethod>
{
    public CustomerPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId)
            .OrderByDescending(pm => pm.CreatedAt);
    }
}

/// <summary>A single saved card, scoped to its owner so one shopper can never see or delete another's.</summary>
public class CustomerPaymentMethodByIdSpecification : Specification<CustomerPaymentMethod>, ISingleResultSpecification<CustomerPaymentMethod>
{
    public CustomerPaymentMethodByIdSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
