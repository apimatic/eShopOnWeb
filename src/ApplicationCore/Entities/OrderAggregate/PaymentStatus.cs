namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The state of the money movement for a <see cref="Payment"/>, mirroring what PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>PayPal is holding the funds (authorization CREATED), nothing captured.</summary>
    Authorized = 0,

    /// <summary>Authorization captured; money has been taken.</summary>
    Captured = 1,

    /// <summary>Authorization voided before capture; the hold was released.</summary>
    Voided = 2,

    /// <summary>Captured, then partially refunded (still refundable up to the remaining amount).</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured, then fully refunded.</summary>
    Refunded = 4
}
