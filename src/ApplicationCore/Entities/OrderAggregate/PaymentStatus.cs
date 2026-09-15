namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    CapturePending,
    CaptureFailed,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
