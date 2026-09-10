namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>Money state of a payment, mirroring the PayPal-owned lifecycle.</summary>
public enum PaymentStatus
{
    /// <summary>Created but not yet authorized.</summary>
    Pending = 0,

    /// <summary>Funds authorized (held) with PayPal.</summary>
    Authorized = 1,

    /// <summary>Authorized funds captured (money taken).</summary>
    Captured = 2,

    /// <summary>Authorization voided before capture; held funds released.</summary>
    Voided = 3,

    /// <summary>Captured then partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured then fully refunded.</summary>
    Refunded = 5,

    /// <summary>The authorization attempt failed.</summary>
    Failed = 6
}
