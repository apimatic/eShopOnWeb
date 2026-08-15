namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an order. Additive to the original eShop model, which had no payment
/// or fulfilment state at all. The money facts that back each transition live on the Payment
/// aggregate; this is the order-side view a shopper and operator act on.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, payment not yet authorized.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (authorized) at PayPal, not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Fulfilled: the held funds have been captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment: the hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then fully refunded.</summary>
    Refunded = 5
}
