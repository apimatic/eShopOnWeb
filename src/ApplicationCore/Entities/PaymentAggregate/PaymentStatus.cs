namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// State of the money movement for an order, mirroring what the payment processor owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>No hold placed yet.</summary>
    Pending = 0,

    /// <summary>Funds held (authorization created), not captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured (money taken).</summary>
    Captured = 2,

    /// <summary>Authorization released before capture; no money moved.</summary>
    Voided = 3,

    /// <summary>Capture fully refunded.</summary>
    Refunded = 4,

    /// <summary>Capture partially refunded.</summary>
    PartiallyRefunded = 5,

    /// <summary>Processor declined / failed the payment.</summary>
    Failed = 6
}
