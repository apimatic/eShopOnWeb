namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an <see cref="Order"/>. This is additive state layered on
/// top of the existing order model to track money movement through PayPal.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal, not captured.</summary>
    Authorized = 1,

    /// <summary>Authorization attempt failed (e.g. declined, or a stale authorization could not be renewed).</summary>
    AuthorizationFailed = 2,

    /// <summary>Order fulfilled; the authorized funds were captured.</summary>
    Fulfilled = 3,

    /// <summary>Authorization released before fulfilment; no money moved.</summary>
    Cancelled = 4,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 5,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 6
}
