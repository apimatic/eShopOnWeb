namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement attached to an <see cref="OrderAggregate.Order"/>.
/// This is additive to the existing order model: an order always has exactly one
/// <see cref="OrderPayment"/> that tracks the PayPal-owned state.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no hold on the money yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Some, but not all, of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>Authorization voided before fulfilment; no money moved.</summary>
    Cancelled = 5,

    /// <summary>A payment attempt failed at PayPal.</summary>
    Failed = 6
}
