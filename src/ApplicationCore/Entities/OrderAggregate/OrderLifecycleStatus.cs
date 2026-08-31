namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderLifecycleStatus
{
    AwaitingPayment,
    Authorized,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
