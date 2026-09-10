namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment.
/// This is additive to the original eShopOnWeb order model, which had no payment state.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed but not yet paid. The starting state.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds have been authorized (held) with the payment processor, but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled and the authorized funds captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled and then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Fulfilled and then partially refunded.</summary>
    PartiallyRefunded = 5
}
