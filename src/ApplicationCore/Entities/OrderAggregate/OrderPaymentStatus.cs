namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    Refunded = 4,
    PartiallyRefunded = 5,
    PaymentFailed = 6
}
