namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to its payment and fulfilment.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the authorization captured; money taken.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5
}
