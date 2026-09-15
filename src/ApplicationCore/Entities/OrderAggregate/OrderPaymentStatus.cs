namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    None = 0,
    AwaitingPayment = 1,
    Authorized = 2,
    Fulfilled = 3,
    Cancelled = 4,
    Refunded = 5,
    PartiallyRefunded = 6
}
