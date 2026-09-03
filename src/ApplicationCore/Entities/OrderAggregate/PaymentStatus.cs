namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    AuthorizationFailed,
    CapturePending,
    Captured,
    CaptureFailed,
    CancellationPending,
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

public enum PaymentOperationStatus
{
    Pending,
    Unknown,
    Completed,
    Failed
}
