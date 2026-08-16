namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// State of the money movement for an order's <see cref="Payment"/>, mirroring what
/// PayPal owns for the hold / capture / refunds.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Payment record created; no authorization placed yet.</summary>
    Pending = 0,

    /// <summary>PayPal is holding the funds (authorization created).</summary>
    Authorized = 1,

    /// <summary>Funds captured in full.</summary>
    Captured = 2,

    /// <summary>Captured then partially refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Authorization voided; no money moved.</summary>
    Voided = 5
}
