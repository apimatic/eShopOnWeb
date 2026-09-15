namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order once payment capabilities are involved. The flow is additive to the
/// existing catalog/basket/order model: a newly placed order starts <see cref="AwaitingPayment"/>.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) with PayPal but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled and then partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled and then refunded in full.</summary>
    Refunded = 5
}
