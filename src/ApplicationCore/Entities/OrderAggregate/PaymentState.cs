namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentState
{
    AwaitingPayment,
    Authorized,
    Captured,
    Cancelled,
    RefundRequested,
    RefundCompleted
}
