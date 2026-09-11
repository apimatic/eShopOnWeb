namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment / payment lifecycle state of an <see cref="Order"/>.
/// This is an additive capability layered on top of the original one-time-commerce order model.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) with the payment provider but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Cancelled before fulfilment; any held funds were released. No money moved.</summary>
    Cancelled = 2,

    /// <summary>Fulfilled by an operator; the held funds have been captured (taken).</summary>
    Fulfilled = 3,

    /// <summary>Fulfilled and then partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled and then fully refunded.</summary>
    Refunded = 5
}
