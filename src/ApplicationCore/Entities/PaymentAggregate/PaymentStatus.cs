namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The state of the money movement for an order, mirroring the PayPal-owned lifecycle.
/// </summary>
public enum PaymentStatus
{
    /// <summary>A PayPal order exists but funds are not yet held.</summary>
    Created = 0,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken).</summary>
    Captured = 2,

    /// <summary>Captured then partially refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Authorization voided; held funds released.</summary>
    Voided = 5,

    /// <summary>The authorization attempt failed / was declined.</summary>
    Failed = 6
}
