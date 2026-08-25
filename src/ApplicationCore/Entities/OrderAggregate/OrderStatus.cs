namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    AwaitingPayment = 0,
    PaymentAuthorized = 1,
    Cancelled = 2,
    Fulfilled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
