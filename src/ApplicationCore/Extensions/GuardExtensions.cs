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
}

public static class GuardExtensions
{
    /// <summary>
    /// Throws when a business rule is broken, e.g. an action that is not allowed in the current
    /// state of an aggregate.
    /// </summary>
    public static void NotAllowed(this IGuardClause guardClause, bool brokenRule, string message)
    {
        if (brokenRule)
        {
            throw new ActionNotAllowedException(message);
        }
    }
}
