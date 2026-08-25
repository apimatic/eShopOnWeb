namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    AwaitingPayment,
    PaymentAuthorized,
    Fulfilled,
    Cancelled
}
