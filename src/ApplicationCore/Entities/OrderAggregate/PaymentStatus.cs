namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// State of a <see cref="Payment"/> mirroring what the payment processor (PayPal) owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Created locally, nothing sent to the processor yet.</summary>
    Pending = 0,

    /// <summary>Money is held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Money has been captured (taken).</summary>
    Captured = 2,

    /// <summary>The hold was released without capturing.</summary>
    Voided = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 5
}
