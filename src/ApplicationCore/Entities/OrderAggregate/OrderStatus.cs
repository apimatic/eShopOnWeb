namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Fulfilled and captured; money taken.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any hold released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then fully refunded.</summary>
    Refunded = 5
}
