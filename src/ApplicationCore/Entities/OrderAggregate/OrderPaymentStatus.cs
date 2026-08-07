namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order's payment. An order is created <see cref="AwaitingPayment"/>,
/// becomes <see cref="Paid"/> once a PayPal capture completes, and <see cref="Refunded"/>
/// after a full refund of that capture.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2
}
