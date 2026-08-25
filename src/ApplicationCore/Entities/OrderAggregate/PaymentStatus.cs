namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
