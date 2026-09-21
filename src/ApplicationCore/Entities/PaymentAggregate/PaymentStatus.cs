namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an <see cref="OrderPayment"/>, tracked independently of the PayPal-side
/// status strings so the application can reason about it without depending on the payment provider.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money has been held or moved yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Funds were captured in full at fulfilment.</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The whole captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The hold was released before capture; no money moved.</summary>
    Canceled = 5,

    /// <summary>Authorization or capture failed.</summary>
    Failed = 6
}
