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
        var valid = (current, requested) switch
        {
            (OrderStatus.Placed, OrderStatus.Dispatched) => true,
            (OrderStatus.Placed, OrderStatus.Cancelled) => true,
            (OrderStatus.Dispatched, OrderStatus.Cancelled) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidOrderStatusTransitionException(current, requested);
    }
}
