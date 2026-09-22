namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of the money movement for an <see cref="Order"/>. An order starts
/// <see cref="AwaitingPayment"/> and only advances through PayPal-backed transitions.
/// </summary>
public enum PaymentState
{
    /// <summary>Order placed; no hold has been taken yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created); money has not moved.</summary>
    Authorized = 1,

    /// <summary>Payment captured at fulfilment; money has moved to the merchant.</summary>
    Fulfilled = 2,

    /// <summary>Hold released before fulfilment; no money ever moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 4,

    /// <summary>Captured payment refunded in part; still refundable up to the captured amount.</summary>
    PartiallyRefunded = 5,

    /// <summary>A PayPal operation failed and left the order without a usable hold/capture.</summary>
    Failed = 6
}
