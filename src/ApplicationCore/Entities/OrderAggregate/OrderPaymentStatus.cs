namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment / fulfilment lifecycle of an <see cref="Order"/>. An order starts
/// <see cref="AwaitingPayment"/> and moves forward as money is held, taken, released or returned.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>The order has been fulfilled and the held funds captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>The hold was released before fulfilment; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Some of the captured amount has been returned.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been returned.</summary>
    Refunded = 5
}
