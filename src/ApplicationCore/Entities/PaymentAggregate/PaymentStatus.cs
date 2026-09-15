namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment, additive to the existing order flow.
/// Money is held at authorization, taken at capture (fulfilment), released on cancel,
/// and returned on refund.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured at fulfilment.</summary>
    Fulfilled = 2,

    /// <summary>Authorization voided before fulfilment; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment fully returned.</summary>
    Refunded = 4,

    /// <summary>Captured payment partly returned.</summary>
    PartiallyRefunded = 5,

    /// <summary>A payment operation failed.</summary>
    Failed = 6
}
