using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Guards;

public static class OrderGuards
{
    public static void InvalidOrderStatus(this IGuardClause guard, OrderStatus actual, OrderStatus expected, string operation, params OrderStatus[] alsoAllowed)
    {
        if (actual != expected && !alsoAllowed.Contains(actual))
        {
            throw new InvalidOrderStateException(
                $"Cannot perform '{operation}' on an order in status '{actual}'. Expected '{expected}'.");
        }
    }
}
