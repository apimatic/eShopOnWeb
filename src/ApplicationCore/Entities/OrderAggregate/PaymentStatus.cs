namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    PendingPayment,
    Authorized,
    Fulfilled,
    Cancelled,
    Refunded
}
