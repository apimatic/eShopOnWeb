namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order stands with respect to taking (and giving back) the shopper's money.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    // A payment attempt is in flight, or its outcome has not been confirmed yet.
    PaymentPending = 1,
    Paid = 2,
    PartiallyRefunded = 3,
    Refunded = 4
}
