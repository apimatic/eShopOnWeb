namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle of an order's payment. An order is created <see cref="AwaitingPayment"/>,
/// transitions to <see cref="Paid"/> once PayPal captures the funds, and to
/// <see cref="Refunded"/> once that capture is fully refunded. <see cref="Failed"/> records
/// a rejected payment attempt while still allowing the shopper to try again.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2,
    Failed = 3
}
