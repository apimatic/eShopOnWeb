namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an eShop order once payments are in play.
/// AwaitingPayment --(pay/authorize)--> Authorized --(fulfil/capture)--> Fulfilled
/// AwaitingPayment|Authorized --(cancel/void)--> Cancelled
/// Fulfilled keeps its status; refund progress is tracked on PaymentRefund records.
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 1,
    Authorized = 2,
    Fulfilled = 3,
    Cancelled = 4
}
