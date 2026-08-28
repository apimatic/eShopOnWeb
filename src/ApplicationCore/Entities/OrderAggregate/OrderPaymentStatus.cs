namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    NotRequired,
    AwaitingPayment,
    Authorizing,
    Authorized,
    PaymentFailed,
    CapturePending,
    Captured,
    RefundPending,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
