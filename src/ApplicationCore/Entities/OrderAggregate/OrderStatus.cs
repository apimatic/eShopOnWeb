namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>Order placed, payment not yet authorized.</summary>
    PendingPayment = 0,

    /// <summary>Funds are authorized (held) with the payment provider; awaiting fulfilment.</summary>
    AwaitingFulfilment = 1,

    /// <summary>Order fulfilled and payment captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
