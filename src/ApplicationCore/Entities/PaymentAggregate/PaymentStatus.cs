namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment. Mirrors the money movement performed against PayPal:
/// a hold is placed at authorization, the money is taken at capture (fulfilment), released on cancel,
/// and returned on refund.
/// </summary>
public enum PaymentStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    PendingAuthorization = 0,

    /// <summary>PayPal is holding the order total; nothing has been captured.</summary>
    Authorized = 1,

    /// <summary>The held funds have been captured (the order was fulfilled).</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before fulfilment; no money moved.</summary>
    Voided = 5,

    /// <summary>Authorization or capture failed.</summary>
    Failed = 6
}
