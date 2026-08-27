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

    public static void InvalidOrderStatusTransition(this IGuardClause guardClause, OrderStatus current, OrderStatus target, int orderId)
    {
        bool valid = target switch
        {
            OrderStatus.PaymentAuthorized => current == OrderStatus.PendingPayment,
            OrderStatus.Fulfilled => current == OrderStatus.PaymentAuthorized,
            OrderStatus.Cancelled => current == OrderStatus.PendingPayment || current == OrderStatus.PaymentAuthorized,
            _ => false
        };

        if (!valid)
            throw new OrderStatusTransitionException(orderId, current, target);
    }
}
