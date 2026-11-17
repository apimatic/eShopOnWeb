namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment. The order itself is created in
/// <see cref="AwaitingPayment"/>; money only moves at <see cref="Authorized"/> (hold),
/// <see cref="Captured"/> (capture) and <see cref="PartiallyRefunded"/>/<see cref="Refunded"/>.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorizing = 1,
    Authorized = 2,
    Capturing = 3,
    Captured = 4,
    Cancelling = 5,
    Cancelled = 6,
    PartiallyRefunded = 7,
    Refunded = 8,
    Failed = 9,
    Unknown = 10
}
