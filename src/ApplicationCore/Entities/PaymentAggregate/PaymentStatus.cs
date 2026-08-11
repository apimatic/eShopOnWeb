namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment. The order's fulfilment state is derived from this:
/// a payment is only <see cref="Captured"/> (money actually taken) once its order is fulfilled.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (authorized) at PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Authorization voided before fulfilment; the hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5
}
