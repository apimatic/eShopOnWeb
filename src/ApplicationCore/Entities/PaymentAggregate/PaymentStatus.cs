namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money behind an order. The "-ing" states are transitional: a PayPal call is in
/// flight (or its outcome is not yet known — see <see cref="OrderPayment.OutcomeUnknownSince"/>).
/// </summary>
public enum PaymentStatus
{
    Authorizing = 0,
    Authorized = 1,
    AuthorizationFailed = 2,
    Reauthorizing = 3,
    Capturing = 4,
    Captured = 5,
    Voiding = 6,
    Voided = 7,
    PartiallyRefunded = 8,
    Refunded = 9
}

public enum RefundStatus
{
    Requested = 0,
    Succeeded = 1,
    Failed = 2
}
