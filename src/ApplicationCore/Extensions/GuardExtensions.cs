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

    public static void InvalidOrderTransition(this IGuardClause guardClause, OrderStatus current, OrderStatus target, string parameterName)
    {
        bool allowed = target switch
        {
            OrderStatus.Dispatched => current == OrderStatus.Placed,
            OrderStatus.Cancelled => current == OrderStatus.Placed || current == OrderStatus.Dispatched,
            _ => false
        };

        if (!allowed)
            throw new InvalidOrderTransitionException($"An order in status '{current}' cannot be transitioned to '{target}'.");
    }
}
