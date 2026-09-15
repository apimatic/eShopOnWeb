namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    AwaitingPayment,
    Authorized,
    PaymentRequired,
    FulfilmentPending,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
