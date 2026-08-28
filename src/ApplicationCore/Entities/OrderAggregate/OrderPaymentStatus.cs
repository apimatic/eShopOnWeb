namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}

public enum OrderFulfillmentStatus
{
    Pending = 0,
    Fulfilled = 1,
    Cancelled = 2
}
