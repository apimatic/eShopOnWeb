namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment/processing state of an order as it moves through payment,
/// fulfilment and cancellation.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no payment taken yet.</summary>
    AwaitingPayment = 0,

    /// <summary>A PayPal authorization (hold on the funds) exists for the order total.</summary>
    Authorized = 1,

    /// <summary>Goods were shipped/handed over and the authorized money was captured.</summary>
    Fulfilled = 2,

    /// <summary>Order was cancelled before fulfilment; any hold was released.</summary>
    Cancelled = 3
}
