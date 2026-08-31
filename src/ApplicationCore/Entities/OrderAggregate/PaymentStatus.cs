namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    CapturePending,
    Captured,
    Cancelled,
    PartiallyRefunded,
    Refunded
}

public enum FulfilmentStatus
{
    Pending,
    Fulfilled,
    Cancelled
}
