namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment / fulfilment state of an <see cref="Order"/>, tracked alongside the money
/// movement performed through PayPal.
/// </summary>
public enum PaymentStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal but not yet captured.</summary>
    Authorized = 1,

    /// <summary>The authorization was captured at fulfilment; the money has moved.</summary>
    Captured = 2,

    /// <summary>The authorization was released before fulfilment; no money moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>The most recent payment attempt failed and left no usable hold.</summary>
    Failed = 6
}
