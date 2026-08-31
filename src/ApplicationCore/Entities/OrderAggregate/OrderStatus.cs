namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>Order placed, waiting for a successful authorization (hold) of the total.</summary>
    PendingPayment = 0,

    /// <summary>Funds are authorized (held) with the payment provider; awaiting fulfilment.</summary>
    AwaitingFulfilment = 1,

    /// <summary>Order fulfilled; the authorized funds were captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any hold on funds was released.</summary>
    Cancelled = 3,

    /// <summary>Order fulfilled and partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Order fulfilled and refunded in full.</summary>
    Refunded = 5
}
