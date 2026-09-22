namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of the payment attached to an eShop order. Additive to the existing
/// order flow — the order itself is unchanged; this tracks the money movement PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no payment attempted yet.</summary>
    PendingPayment = 0,

    /// <summary>A pay request is in flight (local claim written, PayPal call not yet completed).</summary>
    Authorizing = 1,

    /// <summary>PayPal is holding the funds (authorization created), not yet captured.</summary>
    Authorized = 2,

    /// <summary>The authorized funds have been captured — money has moved to the merchant.</summary>
    Fulfilled = 3,

    /// <summary>The authorization was voided before capture — no money moved.</summary>
    Cancelled = 4,

    /// <summary>Some, but not all, of the captured amount has been refunded.</summary>
    PartiallyRefunded = 5,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 6,

    /// <summary>The authorization attempt failed and no hold exists.</summary>
    Failed = 7
}
