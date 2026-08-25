namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    PendingPayment,
    Authorized,
    Fulfilled,
    Cancelled,
    Refunded,
    PartiallyRefunded
}
