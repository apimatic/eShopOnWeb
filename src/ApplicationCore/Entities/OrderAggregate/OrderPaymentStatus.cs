namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order's payment. Orders are created <see cref="AwaitingPayment"/> and move to
/// <see cref="Paid"/> once a PayPal capture completes, then optionally to <see cref="Refunded"/>.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Paid = 1,
    Refunded = 2,
    Failed = 3
}
