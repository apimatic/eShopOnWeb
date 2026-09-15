namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    AwaitingPayment,
    Authorized,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}

public enum FulfilmentStatus
{
    Unfulfilled,
    Fulfilled,
    Cancelled
}

public enum PaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    CapturePending,
    Captured,
    RefundPending,
    PartiallyRefunded,
    Refunded,
    Voided,
    Failed
}
