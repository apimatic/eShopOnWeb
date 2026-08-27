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
    public static void InvalidOrderStatusForPayment(this IGuardClause guardClause, OrderStatus status)
    {
        if (status != OrderStatus.AwaitingPayment)
            throw new PaymentStateException($"Order is not awaiting payment (current status: {status}).");
    }

    public static void InvalidOrderStatusForFulfilment(this IGuardClause guardClause, OrderStatus status)
    {
        if (status != OrderStatus.PaymentAuthorized)
            throw new PaymentStateException($"Order cannot be fulfilled because it is not in a paid/authorized state (current status: {status}).");
    }

    public static void InvalidOrderStatusForCancellation(this IGuardClause guardClause, OrderStatus status)
    {
        if (status == OrderStatus.Fulfilled)
            throw new PaymentStateException("Order has already been fulfilled and captured; issue a refund instead of cancelling.");
        if (status == OrderStatus.Cancelled)
            throw new PaymentStateException("Order is already cancelled.");
    }
}
