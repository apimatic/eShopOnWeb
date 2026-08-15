namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment / payment lifecycle of an <see cref="Order"/>.
/// An order is additive on top of the classic catalog flow: it starts awaiting payment,
/// gets its funds held (authorized), then either has the money taken (fulfilled),
/// released (cancelled) or returned (refunded).
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held with PayPal (authorization). No money has moved.</summary>
    Authorized = 1,

    /// <summary>Money has been captured at fulfilment.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the held funds were released (void). No money moved.</summary>
    Cancelled = 3,

    /// <summary>Part of a captured payment has been returned.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been returned.</summary>
    Refunded = 5
}
