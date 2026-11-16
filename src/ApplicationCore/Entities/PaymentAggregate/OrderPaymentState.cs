namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum OrderPaymentState
{
    AwaitingPayment,
    Authorized,
    Captured,
    Cancelled,
    Failed,
    PartiallyRefunded,
    Refunded,
    OutcomeUnknown
}
