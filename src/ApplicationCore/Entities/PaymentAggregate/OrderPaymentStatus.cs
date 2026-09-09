namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment, additive to the existing order flow.
/// AwaitingPayment -> Authorized -> Fulfilled -> (PartiallyRefunded|Refunded)
/// Authorized -> Cancelled (funds released before any money moved).
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Cancelled = 5
}
