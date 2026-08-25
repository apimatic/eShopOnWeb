using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class OrderStatusGuardExtensions
{
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> ValidTransitions = new()
    {
        (OrderStatus.AwaitingPayment, OrderStatus.PaymentAuthorized),
        (OrderStatus.AwaitingPayment, OrderStatus.Cancelled),
        (OrderStatus.PaymentAuthorized, OrderStatus.Fulfilled),
        (OrderStatus.PaymentAuthorized, OrderStatus.Cancelled)
    };

    public static void InvalidOrderStatusTransition(this IGuardClause guard, OrderStatus current, OrderStatus target)
    {
        if (!ValidTransitions.Contains((current, target)))
        {
            throw new InvalidOrderStateException($"Order cannot move from status '{current}' to '{target}'.");
        }
    }
}
