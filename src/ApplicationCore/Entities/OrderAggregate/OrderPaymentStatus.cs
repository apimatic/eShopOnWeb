namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    /// <summary>The payment provider has not confirmed the outcome of an attempt yet.</summary>
    PaymentPending = 1,
    Paid = 2,
    PartiallyRefunded = 3,
    Refunded = 4
}
