namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// eShopOnWeb order model, which had no notion of payment or fulfilment state.
/// </summary>
public enum OrderState
{
    /// <summary>Order placed but not yet paid. No money has been held.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (funds held) but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order has been fulfilled and the held funds captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
