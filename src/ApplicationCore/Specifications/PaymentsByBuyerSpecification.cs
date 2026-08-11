using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's payments (with refunds), newest first.</summary>
public class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.OrderId);
    }
}
