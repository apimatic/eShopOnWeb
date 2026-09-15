namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The state of the money movement for an order's <see cref="Payment"/>, mirroring the
/// portion of the lifecycle that PayPal owns (authorization, capture, refunds).
/// </summary>
public enum PaymentStatus
{
    /// <summary>A payment record exists but no hold has been placed yet.</summary>
    Pending = 0,

    /// <summary>PayPal is holding the funds (authorization created), not yet captured.</summary>
    Authorized = 1,

    /// <summary>The funds have been captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>The authorization was voided before capture; the hold was released.</summary>
    Voided = 3,

    /// <summary>The captured payment has been partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The captured payment has been fully refunded.</summary>
    Refunded = 5,

    /// <summary>The payment attempt failed and no hold is in place.</summary>
    Failed = 6
}
