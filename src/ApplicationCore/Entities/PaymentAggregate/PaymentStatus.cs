namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an <see cref="OrderPayment"/>. Mirrors the money movement performed
/// against the payment processor (PayPal): a hold is placed at authorization, the money is taken
/// at capture, and given back on cancel (void) or refund.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no hold on the money yet.</summary>
    PendingAuthorization = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds have been taken (captured).</summary>
    Captured = 2,

    /// <summary>Some, but not all, of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The hold was released before capture; no money ever moved.</summary>
    Cancelled = 5,

    /// <summary>Authorization failed and no hold is in place.</summary>
    Failed = 6
}
