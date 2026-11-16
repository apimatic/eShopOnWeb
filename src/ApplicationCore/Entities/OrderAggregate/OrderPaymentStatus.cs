namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment,
    Authorized,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Cancelled,
    Failed,
    CaptureFailed
}
