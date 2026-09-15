namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
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
