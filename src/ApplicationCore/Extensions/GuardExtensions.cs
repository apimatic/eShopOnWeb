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
    public static void InvalidOrderStatusTransition(this IGuardClause guardClause, OrderStatus current, OrderStatus target, string parameterName)
    {
        var allowed = target switch
        {
            OrderStatus.Dispatched => current == OrderStatus.Placed,
            OrderStatus.Cancelled => current == OrderStatus.Placed || current == OrderStatus.Dispatched,
            _ => false
        };

        if (!allowed)
            throw new InvalidOrderStatusTransitionException(current, target);
    }
}
