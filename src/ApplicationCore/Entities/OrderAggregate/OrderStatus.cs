namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment.
/// This is additive to the original eShopOnWeb order flow, which had no status at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled and the authorized funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any hold was released.</summary>
    Cancelled = 3
}
