namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>Order placed, awaiting payment (authorization hold).</summary>
    PendingPayment = 0,

    /// <summary>Funds are authorized (held) at the payment provider but not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled by an operator; funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any hold on funds released.</summary>
    Cancelled = 3
}
