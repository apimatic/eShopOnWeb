namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentStatus
{
    /// <summary>No money has been taken yet; the order can be paid.</summary>
    AwaitingPayment = 0,

    /// <summary>A payment was sent to the processor but its outcome is not yet known. Paying again settles it without a second charge.</summary>
    PaymentPending = 1,

    Paid = 2,
    PartiallyRefunded = 3,
    Refunded = 4
}
