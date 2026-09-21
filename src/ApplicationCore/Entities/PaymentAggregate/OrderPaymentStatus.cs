namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement attached to an <see cref="OrderAggregate.Order"/>.
/// An order with no <see cref="OrderPayment"/> row at all is "awaiting payment"; the row is
/// created the moment payment is attempted.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Local claim written; the PayPal authorization has not completed yet.</summary>
    Pending = 0,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds were captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>The hold was released before fulfilment; no money moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>The authorization attempt failed; the shopper may retry.</summary>
    Failed = 6
}

/// <summary>State of a single refund against a captured payment.</summary>
public enum RefundState
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
