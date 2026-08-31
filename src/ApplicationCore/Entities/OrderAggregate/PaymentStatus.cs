namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    NotRequired,
    AwaitingPayment,
    Pending,
    Authorized,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided,
    Cancelled,
    Expired
}
