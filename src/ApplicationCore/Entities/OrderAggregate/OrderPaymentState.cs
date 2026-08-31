namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentState
{
    AwaitingPayment,
    Authorized,
    CapturePending,
    Fulfilled,
    Cancelled,
    PaymentActionRequired,
    PartiallyRefunded,
    Refunded
}
