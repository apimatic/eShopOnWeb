namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment, tracked independently of the catalog order it settles.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money has been held yet.</summary>
    PendingAuthorization = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before fulfilment; no money moved.</summary>
    Voided = 5,

    /// <summary>Authorization failed and no hold exists.</summary>
    Failed = 6
}
