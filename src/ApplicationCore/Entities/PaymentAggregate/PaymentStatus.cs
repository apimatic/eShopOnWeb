namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement state of a <see cref="Payment"/>, mirroring where the funds are in the
/// PayPal authorize/capture/refund lifecycle.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds are held (authorized) but have not been captured.</summary>
    Authorized = 0,

    /// <summary>The held funds have been captured (taken).</summary>
    Captured = 1,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 2,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 3,

    /// <summary>The authorization was voided before capture; the hold was released.</summary>
    Voided = 4
}
