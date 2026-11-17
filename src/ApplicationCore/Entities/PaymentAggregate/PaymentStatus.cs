namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// State of the money movement for an order. This is separate from the order fulfilment state.
/// </summary>
public enum PaymentStatus
{
    AwaitingAuthorization = 0,
    Authorized = 1,
    CapturePending = 2,
    Captured = 3,
    Canceled = 4,
    PartiallyRefunded = 5,
    Refunded = 6,
    Failed = 7
}
