namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an <see cref="Order"/>. Additive to the original
/// eShopOnWeb order, which had no payment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed; no money held yet. Awaiting a call to authorize (pay).</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) with PayPal but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Fulfilled by an operator; the held funds have been captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the held funds were released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment was refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment was refunded in full.</summary>
    Refunded = 5
}
