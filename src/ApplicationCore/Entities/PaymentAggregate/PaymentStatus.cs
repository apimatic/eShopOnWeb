namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an <see cref="OrderPayment"/>, mirroring the money movement at PayPal.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet (awaiting <c>/pay</c>).</summary>
    PendingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal; not yet captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured at fulfilment; money has moved to the merchant.</summary>
    Captured = 2,

    /// <summary>Authorization voided before capture; the hold was released.</summary>
    Voided = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5,

    /// <summary>A payment attempt failed at PayPal.</summary>
    Failed = 6
}
