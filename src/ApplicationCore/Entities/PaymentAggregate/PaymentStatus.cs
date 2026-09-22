namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an <see cref="OrderPayment"/>. This is eShop's own view of the payment;
/// the authoritative PayPal-owned status strings are stored alongside on the entity.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet. Awaiting authorization (pay).</summary>
    PendingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Captured, then partially refunded — still refundable up to the remaining amount.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured, then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Authorization voided before fulfilment — no money moved.</summary>
    Cancelled = 5,

    /// <summary>Authorization/capture failed and cannot proceed.</summary>
    Failed = 6
}
