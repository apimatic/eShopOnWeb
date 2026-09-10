namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of an order's payment as this application tracks it. This is our own state, distinct
/// from (but driven by) the PayPal-owned statuses stored alongside it on <see cref="Payment"/>.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created) but not captured.</summary>
    Authorized = 1,

    /// <summary>Money taken (PayPal capture completed).</summary>
    Captured = 2,

    /// <summary>Authorization voided before capture; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5,

    /// <summary>Authorization attempt failed.</summary>
    Failed = 6
}
