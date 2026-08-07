namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order's payment. Orders are placed <see cref="AwaitingPayment"/>, become
/// <see cref="Paid"/> once PayPal captures the funds, and <see cref="Refunded"/> after a full refund.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2
}
