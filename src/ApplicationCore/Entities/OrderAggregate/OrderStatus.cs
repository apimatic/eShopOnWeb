namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment. This is additive to the
/// original eShopOnWeb order model, which had no payment or fulfilment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal but not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Operator fulfilled the order; the authorization was captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the authorization was voided and held funds released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then partly refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then refunded in full.</summary>
    Refunded = 5
}
