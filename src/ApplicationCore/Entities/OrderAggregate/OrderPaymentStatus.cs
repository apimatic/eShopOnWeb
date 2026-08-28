namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    PayerActionRequired,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided,
    PaymentFailed,
    AuthorizationExpired
}
