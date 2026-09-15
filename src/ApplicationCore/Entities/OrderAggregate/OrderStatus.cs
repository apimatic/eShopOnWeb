namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an <see cref="Order"/>. This is additive to the existing
/// catalog/basket/order flow: an order created through checkout starts life
/// <see cref="AwaitingPayment"/> and only ever moves forward through these states.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no funds have been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (funds held) but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order has been fulfilled by an operator and the money has been captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
