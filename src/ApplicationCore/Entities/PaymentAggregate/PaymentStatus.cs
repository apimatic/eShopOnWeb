namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment. The order starts awaiting payment; a hold is placed
/// (Authorized); the money is taken at fulfilment (Captured); a hold can be released before
/// fulfilment (Cancelled); a captured payment can be refunded in part or in full.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Failed = 6
}
