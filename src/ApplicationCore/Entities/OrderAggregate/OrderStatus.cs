namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    FulfilmentPending = 2,
    Fulfilled = 3,
    Cancelled = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}

public enum PaymentStatus
{
    Creating = 0,
    Authorizing = 1,
    Authorized = 2,
    CapturePending = 3,
    Captured = 4,
    Voided = 5,
    PartiallyRefunded = 6,
    Refunded = 7,
    Failed = 8
}

public enum PaymentRefundStatus
{
    Requested = 0,
    Pending = 1,
    Completed = 2,
    Failed = 3
}
