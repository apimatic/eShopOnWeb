namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an <see cref="Order"/>. This is additive to the
/// original eShopOnWeb order flow, which had no payment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (funds held) but not captured.</summary>
    Authorized = 1,

    /// <summary>The order has been fulfilled and the money captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment and the held funds released.</summary>
    Cancelled = 3,

    /// <summary>Part of a captured order has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
