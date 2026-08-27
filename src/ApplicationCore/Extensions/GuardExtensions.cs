using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Ardalis.GuardClauses;

public static class BasketGuards
{
    public static void EmptyBasketOnCheckout(this IGuardClause guardClause, IReadOnlyCollection<BasketItem> basketItems)
    {
        if (!basketItems.Any())
            throw new EmptyBasketOnCheckoutException();
    }

    public static void InvalidOrderStatusTransition(this IGuardClause guardClause, bool invalidTransition, string parameterName)
    {
        if (invalidTransition)
            throw new InvalidOrderStatusTransitionException($"The order's current status does not allow this transition ({parameterName}).");
    }
}
