namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an <see cref="Order"/>. This is additive state layered on top of the
/// original one-time-commerce order: an order now starts awaiting payment, has its funds held on
/// authorization, and is only fulfilled (captured), cancelled (voided) or refunded by an operator flow.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds have been authorized (held) but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Operator fulfilled the order; the held funds were captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5
}
