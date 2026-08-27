using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Ardalis.GuardClauses;

public static class BasketGuards
{
    public static void EmptyBasketOnCheckout(this IGuardClause guardClause, IReadOnlyCollection<BasketItem> basketItems)
    {
        if (!basketItems.Any())
            throw new EmptyBasketOnCheckoutException();
    }
}

public static class OrderGuards
{
    public static void InvalidOrderTransition(this IGuardClause guardClause, OrderStatus current, OrderStatus target, int orderId)
    {
        bool allowed = target switch
        {
            OrderStatus.Dispatched => current == OrderStatus.Placed,
            OrderStatus.Cancelled => current is OrderStatus.Placed or OrderStatus.Dispatched,
            _ => false
        };

        if (!allowed)
            throw new OrderStateException($"Order {orderId} cannot transition from {current} to {target}.");
    }
}
