namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment, tracking the money movement PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no hold yet.</summary>
    PendingPayment = 0,

    /// <summary>Funds held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds taken (captured) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Authorization voided before capture; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5
}
