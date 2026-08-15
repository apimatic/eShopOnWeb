namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement state of a <see cref="Payment"/>, mirroring what PayPal owns for the
/// authorization, capture and refunds.
/// </summary>
public enum PaymentStatus
{
    /// <summary>An authorization attempt is in progress but not yet confirmed.</summary>
    PendingAuthorization = 0,

    /// <summary>Funds are held (authorized) but not captured. No money has moved.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) in full.</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before capture; the hold was released.</summary>
    Voided = 5,

    /// <summary>The authorization attempt failed and no funds are held.</summary>
    Failed = 6
}
