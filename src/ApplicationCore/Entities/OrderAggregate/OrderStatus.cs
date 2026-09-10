namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment / fulfilment lifecycle of an <see cref="Order"/>. This is additive state that the
/// original eShopOnWeb order did not carry: an order now moves from awaiting payment, through an
/// authorization hold, to being fulfilled (captured), and can then be cancelled or refunded.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (held) on the shopper's card. No money has moved.</summary>
    Authorized = 1,

    /// <summary>The order was fulfilled by an operator and the held funds were captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
