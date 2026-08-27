namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>The order was placed and is waiting for a payment authorization.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total is authorized (held) with the payment provider.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order is fulfilled and the payment captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured payment has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The captured payment has been refunded in full.</summary>
    Refunded = 5
}
