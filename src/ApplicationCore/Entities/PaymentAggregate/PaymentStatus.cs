namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment. Transitions:
/// AwaitingPayment → Authorized → (Captured → PartiallyRefunded/Refunded) | Cancelled;
/// AwaitingPayment → Cancelled.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Fulfilled: funds captured.</summary>
    Captured = 2,

    /// <summary>Cancelled before fulfilment: the authorization hold was released.</summary>
    Cancelled = 3,

    /// <summary>Captured then partly refunded (refunded &lt; captured).</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured then fully refunded (refunded == captured).</summary>
    Refunded = 5
}
