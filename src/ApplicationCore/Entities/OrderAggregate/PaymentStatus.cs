namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment lifecycle of an <see cref="Order"/>. Orders are additive one-time commerce:
/// they are created awaiting payment, become paid once captured through the payment gateway,
/// and can subsequently be fully refunded.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 1,
    Paid = 2,
    Refunded = 3
}
