namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement lifecycle of an order, layered additively on top of the existing catalog/order flow.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not taken.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; funds captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; held funds released (voided).</summary>
    Cancelled = 3,

    /// <summary>Captured payment partly refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 5,

    /// <summary>A payment operation failed and left the order unpaid.</summary>
    Failed = 6
}
