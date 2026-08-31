using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public static class OrderStatusTransitionGuard
{
    public static void InvalidOrderStatusTransition(this IGuardClause guardClause, OrderStatus current, OrderStatus target)
    {
        bool allowed = target switch
        {
            OrderStatus.Dispatched => current == OrderStatus.Placed,
            OrderStatus.Cancelled => current == OrderStatus.Placed || current == OrderStatus.Dispatched,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"Order cannot move from {current} to {target}.");
        }
    }
}
