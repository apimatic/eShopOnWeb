namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment / payment lifecycle state for an <see cref="Order"/>.
/// This is additive to the original eShopOnWeb model, which had no payment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with the payment processor, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Operator fulfilled the order; funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the authorization hold was released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Fulfilled then partially refunded.</summary>
    PartiallyRefunded = 5
}
