namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment / payment lifecycle of an <see cref="Order"/>.
/// This is additive to the original one-time-commerce flow: an order starts
/// <see cref="AwaitingPayment"/> and moves through the money-movement states below.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no payment has been authorized yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds have been authorized (held) with PayPal but not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order was fulfilled and the authorized funds were captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any hold was released.</summary>
    Cancelled = 3
}
