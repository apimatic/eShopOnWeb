using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments (with refunds) owned by a buyer — used to attach payment state to my-orders.</summary>
public class PaymentsByBuyerSpecification : Specification<Payment>
{
    public PaymentsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}
