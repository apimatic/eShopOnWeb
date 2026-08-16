namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment / payment lifecycle of an <see cref="Order"/>. eShopOnWeb orders originally carried
/// no payment state at all; this drives the pay -> fulfil -> cancel/refund operator flows.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Operator fulfilled the order; the authorization was captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then partially refunded; still has captured funds not yet returned.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then fully refunded.</summary>
    Refunded = 5
}
