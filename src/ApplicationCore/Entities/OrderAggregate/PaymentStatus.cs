namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    AuthorizationDenied,
    Authorized,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided,
    Cancelled
}
