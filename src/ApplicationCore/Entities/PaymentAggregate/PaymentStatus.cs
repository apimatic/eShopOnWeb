namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of an order's payment/fulfilment. This is the state eShopOnWeb did not previously
/// carry: an order now starts <see cref="AwaitingPayment"/> and moves through the money-movement steps.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal; not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; funds captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the hold was released, no money moved.</summary>
    Canceled = 3,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 4,

    /// <summary>Captured payment partly refunded; still refundable up to the captured amount.</summary>
    PartiallyRefunded = 5,

    /// <summary>Authorization attempt failed (declined / could not be renewed).</summary>
    AuthorizationFailed = 6
}
