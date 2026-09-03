using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments (operator-wide), with owned refunds, for reconciliation.</summary>
public sealed class AllPaymentsSpecification : Specification<Payment>
{
    public AllPaymentsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}

/// <summary>A single order by id, with its items, for building a payment view.</summary>
public sealed class OrderWithItemsByIdSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithItemsByIdSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
             .Include(o => o.OrderItems)
                 .ThenInclude(i => i.ItemOrdered);
    }
}

/// <summary>The payment for a given order. Owned refunds are included automatically by EF.</summary>
public sealed class PaymentByOrderIdSpecification : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
             .Include(p => p.Refunds);
    }
}

/// <summary>All payments belonging to a buyer, newest first.</summary>
public sealed class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
             .Include(p => p.Refunds)
             .OrderByDescending(p => p.CreatedAt);
    }
}

/// <summary>All saved cards belonging to a buyer, newest first.</summary>
public sealed class SavedCardsByBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
             .OrderByDescending(c => c.CreatedAt);
    }
}

/// <summary>A single saved card by id and owner (owner scoping is part of the query).</summary>
public sealed class SavedCardByIdSpecification : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(c => c.Id == paymentMethodId && c.BuyerId == buyerId);
    }
}
