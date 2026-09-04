using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsForBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsForBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}

public class SavedPaymentMethodByBuyerAndExternalIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByBuyerAndExternalIdSpecification(string buyerId, string externalId)
    {
        Query.Where(p => p.BuyerId == buyerId && p.ExternalId == externalId);
    }
}

/// <summary>Latest paid orders of a buyer, used to find a reusable network transaction reference.</summary>
public class BuyerPaidOrdersSpecification : Specification<Order>
{
    public BuyerPaidOrdersSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate);
    }
}
