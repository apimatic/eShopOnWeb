namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle of an order's payment. An order starts <see cref="AwaitingPayment"/>,
/// becomes <see cref="Paid"/> once PayPal captures the funds, and <see cref="Refunded"/>
/// once that capture has been fully refunded.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2
}
