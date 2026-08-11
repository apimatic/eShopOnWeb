namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle state of an order's payment. This is the additive "payment / fulfilment state"
/// that the original <see cref="OrderAggregate.Order"/> aggregate does not carry.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created) but not captured.</summary>
    Authorized = 1,

    /// <summary>Money has been taken (authorization captured) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Authorization voided before capture; the hold was released and no money moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>A gateway operation failed and left the payment in an unusable state.</summary>
    Failed = 6
}
