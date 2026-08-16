namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. This is additive state layered on top of the
/// original eShopOnWeb order model, which had no notion of payment or fulfilment.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (a hold placed) but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order has been fulfilled and the authorized funds captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any hold on funds was released.</summary>
    Cancelled = 3
}
