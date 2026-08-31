namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    Pending,
    Authorized,
    Captured,
    Voided,
    PartiallyRefunded,
    Refunded
}
