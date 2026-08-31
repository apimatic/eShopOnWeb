namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    AuthorizationRenewalRequired,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided
}
