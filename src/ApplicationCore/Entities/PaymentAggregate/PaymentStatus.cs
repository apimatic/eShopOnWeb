namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of a payment for an order. This is the fulfilment/money-movement
/// state that the base <see cref="OrderAggregate.Order"/> deliberately does not carry.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; the held funds have been captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then fully refunded.</summary>
    Refunded = 5,

    /// <summary>Authorization was declined or could not be completed.</summary>
    Failed = 6
}
