namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment. The money movement mirrors PayPal:
/// authorize (hold) at checkout, capture (take) at fulfilment, refund on return.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no hold on the money yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal; not yet captured.</summary>
    Authorized = 1,

    /// <summary>Payment captured — the money has been taken.</summary>
    Fulfilled = 2,

    /// <summary>Authorization voided before fulfilment; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 4,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 5,

    /// <summary>Authorization or capture failed at PayPal.</summary>
    Failed = 6
}
