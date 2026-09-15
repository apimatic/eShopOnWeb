namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Coarse-grained lifecycle state of an <see cref="Order"/> as it moves through
/// payment and fulfilment. The fine-grained PayPal state (hold / capture / refunds)
/// lives on the associated <see cref="Payment"/>.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any hold was released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Fulfilled then refunded in part.</summary>
    PartiallyRefunded = 5
}
