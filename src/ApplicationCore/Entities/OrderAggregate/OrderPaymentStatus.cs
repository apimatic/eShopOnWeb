namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment,
    Authorized,
    CapturePending,
    CaptureFailed,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided
}
