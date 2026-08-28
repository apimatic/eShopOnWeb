namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    PaymentFailed = 1,
    Authorized = 2,
    Captured = 3,
    RefundPending = 4,
    PartiallyRefunded = 5,
    Refunded = 6,
    Cancelled = 7
}
