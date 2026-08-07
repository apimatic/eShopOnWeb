using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All saved cards belonging to a single buyer, newest first.</summary>
public class CustomerPaymentMethodsSpecification : Specification<PaymentMethod>
{
    public CustomerPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId)
             .OrderByDescending(pm => pm.CreatedDate);
    }
}

/// <summary>A single saved card, scoped to its owner so it is never returned to another buyer.</summary>
public class PaymentMethodForBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodForBuyerSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(pm => pm.BuyerId == buyerId && pm.Id == paymentMethodId);
    }
}

/// <summary>A buyer's saved card by its PayPal vault token; used to dedupe repeated save requests.</summary>
public class PaymentMethodByVaultIdSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByVaultIdSpecification(string buyerId, string vaultId)
    {
        Query.Where(pm => pm.BuyerId == buyerId && pm.VaultId == vaultId);
    }
}
