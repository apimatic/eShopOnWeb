namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment state as eShop understands it, mirroring the money movement that PayPal owns.
/// </summary>
public enum PaymentState
{
    /// <summary>A payment record exists but no money has been held yet.</summary>
    Pending = 0,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken).</summary>
    Captured = 2,

    /// <summary>The authorization hold was released without capturing.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
