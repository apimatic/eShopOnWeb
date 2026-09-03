namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    CapturePending,
    Captured,
    Voided,
    PartiallyRefunded,
    Refunded,
    AuthorizationPending,
    RefundPending
}
