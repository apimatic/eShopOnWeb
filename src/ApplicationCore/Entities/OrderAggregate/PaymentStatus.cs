namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Status of the money movement for an order, mirroring the lifecycle PayPal owns for an
/// authorization/capture/refund.
/// </summary>
public enum PaymentStatus
{
    /// <summary>A payment record exists but no hold has been placed yet.</summary>
    Pending = 0,

    /// <summary>Funds are held (PayPal authorization created) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) in full.</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The entire captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before capture; no money moved.</summary>
    Voided = 5
}
