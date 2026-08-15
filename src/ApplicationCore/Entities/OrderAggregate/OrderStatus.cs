namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an <see cref="Order"/>. This is additive state layered on top of
/// the original one-time-commerce order: eShopOnWeb historically ended checkout by writing an
/// order row with no payment or fulfilment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet. The initial state of every order.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (funds held) but not yet captured.</summary>
    Authorized = 1,

    /// <summary>The operator fulfilled the order; the held funds have been captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
