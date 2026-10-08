namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment state of an order, derived from its payment attempts and refunds.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment,
    PaymentPending,
    Paid,
    PartiallyRefunded,
    Refunded
}
