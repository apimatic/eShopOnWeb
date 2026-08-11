using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop payments that reached PayPal (have a PayPal order id) and were created within the given range.
/// Used to line eShop's own record up against PayPal's transaction report during reconciliation.
/// </summary>
public class OrderPaymentsInRangeSpec : Specification<OrderPayment>
{
    public OrderPaymentsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(p => p.PayPalOrderId != null && p.CreatedAt >= from && p.CreatedAt <= to)
            .Include(p => p.Refunds);
    }
}
