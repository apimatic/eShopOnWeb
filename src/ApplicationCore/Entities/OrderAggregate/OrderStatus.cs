namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    PendingPayment = 0,
    PaymentAuthorized = 1,
    Fulfilled = 2,
    Cancelled = 3,
    Refunded = 4,
    PartiallyRefunded = 5
}
