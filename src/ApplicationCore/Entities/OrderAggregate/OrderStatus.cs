namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment.
/// Additive to the original one-time-commerce flow: an order now starts
/// <see cref="AwaitingPayment"/> and moves through the money-movement states.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed; no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal but not captured.</summary>
    Authorized = 1,

    /// <summary>Operator fulfilled the order; the authorization was captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then partly refunded; part of the captured amount remains with the merchant.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then fully refunded; the entire captured amount was returned.</summary>
    Refunded = 5
}
