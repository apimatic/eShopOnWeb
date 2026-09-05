namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order, from the moment it is placed until it is fulfilled or cancelled.
/// Payment movement is tracked separately on the order's <c>OrderPayment</c>.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order has been placed; the shopper has not paid for it yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total is on hold at the payment processor but has not been taken.</summary>
    Authorized = 1,

    /// <summary>The order has been fulfilled and the held money has been taken.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment and the held money was released.</summary>
    Cancelled = 3
}
