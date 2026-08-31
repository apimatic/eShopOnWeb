namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>Order placed, awaiting payment authorization.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are on hold with the payment provider.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any hold released.</summary>
    Cancelled = 3
}
