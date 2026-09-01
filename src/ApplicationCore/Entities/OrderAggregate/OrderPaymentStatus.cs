namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment/fulfilment lifecycle of an order. An order starts awaiting payment; paying places
/// an authorization hold; fulfilling captures the money; cancelling before fulfilment voids
/// the hold; refunds apply after capture.
/// </summary>
public enum OrderPaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    CapturePending = 2,
    Captured = 3,
    Cancelled = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
