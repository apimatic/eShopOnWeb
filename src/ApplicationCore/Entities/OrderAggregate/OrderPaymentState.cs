namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentState
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    CapturePending,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
