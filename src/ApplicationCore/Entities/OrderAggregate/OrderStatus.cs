namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an order with respect to payment and fulfilment.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, waiting for the shopper to authorize payment.</summary>
    PendingPayment = 0,

    /// <summary>Payment authorized (funds on hold), awaiting fulfilment.</summary>
    AwaitingFulfilment = 1,

    /// <summary>Order fulfilled and payment captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any held funds released.</summary>
    Cancelled = 3,

    /// <summary>Order fulfilled, then partly refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Order fulfilled, then refunded in full.</summary>
    Refunded = 5
}
