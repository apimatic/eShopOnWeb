namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>Money-movement state that PayPal owns, mirrored locally so a later request can act on it.</summary>
public enum PaymentStatus
{
    /// <summary>A payment record exists but no hold has been placed yet.</summary>
    Pending = 0,

    /// <summary>Funds are held (authorized) at PayPal.</summary>
    Authorized = 1,

    /// <summary>The hold was released; no money moved.</summary>
    Voided = 2,

    /// <summary>Funds were captured (taken).</summary>
    Captured = 3,

    /// <summary>Captured, then part of the amount was refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured, then the full captured amount was refunded.</summary>
    Refunded = 5,

    /// <summary>The authorization or capture failed.</summary>
    Failed = 6
}
