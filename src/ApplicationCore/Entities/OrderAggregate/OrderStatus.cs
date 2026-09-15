namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// eShopOnWeb order model, which had no concept of payment or fulfilment state.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no payment hold has been taken yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (funds held) but not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order has been fulfilled and the payment captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
