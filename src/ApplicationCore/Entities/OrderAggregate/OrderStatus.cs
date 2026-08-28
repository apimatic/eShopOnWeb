namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    Created,
    AwaitingPayment,
    Authorized,
    AuthorizationExpired,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
