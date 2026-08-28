namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided,
    Cancelled
}

public enum FulfillmentStatus
{
    Pending,
    Fulfilled,
    Cancelled
}
