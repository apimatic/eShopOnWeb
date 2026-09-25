namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an order. This is the state the reference app previously lacked —
/// an <see cref="OrderAggregate.Order"/> only recorded that it had been placed.
/// </summary>
public enum PaymentState
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created) but not taken.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 4,

    /// <summary>Captured payment partially refunded.</summary>
    PartiallyRefunded = 5
}
