namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment lifecycle of an <see cref="Order"/>. An order is created awaiting payment,
/// becomes <see cref="Paid"/> once PayPal captures the payment, and <see cref="Refunded"/>
/// once that captured payment is refunded in full.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2
}
