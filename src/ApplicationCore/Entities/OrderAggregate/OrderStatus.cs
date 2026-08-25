namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    PendingPayment = 1,
    PaymentAuthorized = 2,
    Fulfilled = 3,
    Cancelled = 4,
    PartiallyRefunded = 5,
    FullyRefunded = 6
}
