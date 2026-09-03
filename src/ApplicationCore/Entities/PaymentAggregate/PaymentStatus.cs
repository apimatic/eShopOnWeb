namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of a <see cref="Payment"/> as money moves through PayPal.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet — awaiting authorization.</summary>
    PendingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal, not yet taken.</summary>
    Authorized = 1,

    /// <summary>The authorized funds were captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Cancelled before fulfilment — the hold was released, no money moved.</summary>
    Canceled = 3,

    /// <summary>A captured payment was refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>A captured payment was refunded in full.</summary>
    Refunded = 5,

    /// <summary>A payment operation failed at PayPal.</summary>
    Failed = 6
}
