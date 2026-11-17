namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    Cancelled,
    PartiallyRefunded,
    Refunded,
    Failed
}

public enum FulfilmentStatus
{
    NotFulfilled,
    Fulfilled,
    Cancelled
}

public enum RefundStatus
{
    Pending,
    Succeeded,
    Failed
}
