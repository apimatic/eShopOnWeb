namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of an <see cref="OrderPayment"/>, tracking the money movement that follows a real
/// card payment: a hold at checkout, a capture at fulfilment, a release on cancel, or a return on refund.
/// </summary>
public enum PaymentStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (funds held) but not captured.</summary>
    Authorized = 1,

    /// <summary>The authorization was captured at fulfilment; money has moved to the merchant.</summary>
    Fulfilled = 2,

    /// <summary>The authorization was voided before fulfilment; the held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured payment has been returned to the shopper.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been returned to the shopper.</summary>
    Refunded = 5
}
