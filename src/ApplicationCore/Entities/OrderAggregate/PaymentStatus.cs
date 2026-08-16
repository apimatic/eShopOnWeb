namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// State of the money movement for an order, mirroring the PayPal-owned lifecycle
/// (hold -> capture -> refund) so a later request can act on it.
/// </summary>
public enum PaymentStatus
{
    /// <summary>A <see cref="Payment"/> record exists but nothing has been authorized yet.</summary>
    Pending = 0,

    /// <summary>Funds are held (authorized) with PayPal; not captured.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) from the shopper.</summary>
    Captured = 2,

    /// <summary>The authorization was voided; the hold was released and no money moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
