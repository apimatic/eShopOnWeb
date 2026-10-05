using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpec : Specification<Payment>, ISingleResultSpecification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsByOrderIdsSpec : Specification<Payment>
{
    public PaymentsByOrderIdsSpec(params int[] orderIds)
    {
        Query.Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}

/// <summary>Payments that moved money (were captured) no later than <paramref name="to"/>, with their refunds.</summary>
public class PaymentsCapturedBeforeSpec : Specification<Payment>
{
    public PaymentsCapturedBeforeSpec(DateTimeOffset to)
    {
        Query.Where(p => p.CapturedAt != null && p.CapturedAt <= to)
            .Include(p => p.Refunds);
    }
}

/// <summary>The buyer's saved cards; removed ones are excluded unless asked for.</summary>
public class PaymentMethodsForBuyerSpec : Specification<PaymentMethod>
{
    public PaymentMethodsForBuyerSpec(string buyerId, bool includeRemoved = false)
    {
        Query.Where(m => m.BuyerId == buyerId);
        if (!includeRemoved)
        {
            Query.Where(m => m.RemovedAt == null);
        }
        Query.OrderBy(m => m.Id);
    }
}

/// <summary>A saved card by id, only if it belongs to the buyer.</summary>
public class PaymentMethodForBuyerSpec : Specification<PaymentMethod>, ISingleResultSpecification<PaymentMethod>
{
    public PaymentMethodForBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(m => m.Id == paymentMethodId && m.BuyerId == buyerId);
    }
}

public class PaymentMethodBySaveKeySpec : Specification<PaymentMethod>, ISingleResultSpecification<PaymentMethod>
{
    public PaymentMethodBySaveKeySpec(string buyerId, string saveRequestKey)
    {
        Query.Where(m => m.BuyerId == buyerId && m.SaveRequestKey == saveRequestKey);
    }
}
