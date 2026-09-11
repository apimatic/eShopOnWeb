namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// State of a PayPal-backed payment, mirroring the PayPal-owned lifecycle
/// (authorization held -> captured -> refunded / voided).
/// </summary>
public enum PaymentStatus
{
    /// <summary>Created locally but no hold placed with PayPal yet.</summary>
    Pending = 0,

    /// <summary>Funds authorized (held) with PayPal, not captured.</summary>
    Authorized = 1,

    /// <summary>Authorization captured (money taken).</summary>
    Captured = 2,

    /// <summary>Authorization voided before capture; held funds released.</summary>
    Voided = 3,

    /// <summary>Captured then partly refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured then refunded up to the captured amount.</summary>
    Refunded = 5,

    /// <summary>Authorization attempt failed / was denied.</summary>
    Failed = 6
}
