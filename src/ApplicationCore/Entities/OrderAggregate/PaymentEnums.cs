namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of a <see cref="Payment"/>, mirroring the money movement PayPal performs
/// against the order (hold, capture, refunds, release).
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>The hold has been captured (money taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Some, but not all, of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided (released) before any capture; no money moved.</summary>
    Voided = 5
}

/// <summary>
/// How the shopper funded the payment.
/// </summary>
public enum PaymentSourceType
{
    /// <summary>A one-off card entered for this payment only.</summary>
    Card = 1,

    /// <summary>One of the shopper's previously saved (vaulted) cards.</summary>
    SavedCard = 2
}
