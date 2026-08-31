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
    public static void InvalidOrderStatusTransition(this IGuardClause guardClause, OrderStatus current, OrderStatus requested)
    {
        bool allowed = requested switch
        {
            OrderStatus.Dispatched => current == OrderStatus.Placed,
            // Cancellation must also reach orders already dispatched, so a queued
            // delivery follow-up can still be called off before it goes out.
            OrderStatus.Cancelled => current is OrderStatus.Placed or OrderStatus.Dispatched,
            _ => false
        };

        if (!allowed)
            throw new InvalidOrderStatusTransitionException(current, requested);
    }
}
