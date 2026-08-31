namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentLifecycleStatus
{
    None,
    AwaitingPayment,
    Authorizing,
    Authorized,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided,
    Failed
}
