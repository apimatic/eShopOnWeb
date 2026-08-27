namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>The order was placed but no payment authorization exists yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total is authorized (funds on hold) with the payment provider.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order is fulfilled and the authorized funds were captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>The captured payment was partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The captured payment was refunded in full.</summary>
    Refunded = 5
}
