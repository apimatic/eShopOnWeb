namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment. This is additive state
/// layered on top of the original one-time-commerce order model.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Fulfilled: the authorization has been captured and money taken.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then fully refunded.</summary>
    Refunded = 5
}
