namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>Order placed, waiting for the shopper to pay (authorize).</summary>
    PendingPayment = 0,

    /// <summary>Funds are on hold with the payment provider; awaiting fulfilment.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled; funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
