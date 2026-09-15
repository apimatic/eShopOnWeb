namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment. This is the payment/fulfilment
/// state that the base <see cref="OrderAggregate.Order"/> deliberately does not carry.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; the held funds have been captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Captured then partially refunded; further refunds may remain possible.</summary>
    PartiallyRefunded = 5
}
