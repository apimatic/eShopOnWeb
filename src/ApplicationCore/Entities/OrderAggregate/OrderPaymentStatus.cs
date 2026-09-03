namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    NotRequired,
    AwaitingPayment,
    Authorizing,
    Authorized,
    PayerActionRequired,
    Capturing,
    Captured,
    RefundPending,
    PartiallyRefunded,
    Refunded,
    Voided,
    Failed
}
