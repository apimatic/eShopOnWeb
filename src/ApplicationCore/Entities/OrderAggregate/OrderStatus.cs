namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>Order has been placed but no payment has been authorized yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order has been fulfilled and the held funds have been captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Some, but not all, of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The entire captured amount has been refunded.</summary>
    Refunded = 5,
}
