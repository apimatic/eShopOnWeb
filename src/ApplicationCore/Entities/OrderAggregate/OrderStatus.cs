namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// eShopOnWeb order model, which had no payment or fulfilment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no payment hold exists yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (funds held) but not captured.</summary>
    Authorized = 1,

    /// <summary>The order has been fulfilled and the held funds captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any hold was released.</summary>
    Cancelled = 3
}
