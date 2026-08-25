namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    PendingPayment,
    PaymentAuthorized,
    Fulfilled,
    Cancelled,
    Refunded
}
