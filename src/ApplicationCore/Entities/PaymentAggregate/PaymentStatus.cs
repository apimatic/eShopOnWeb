namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of a <see cref="Payment"/>, which decorates an <see cref="OrderAggregate.Order"/>
/// with the money-movement state that PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (PayPal authorization created) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds taken (PayPal capture completed).</summary>
    Captured = 2,

    /// <summary>Captured, then part of the captured amount returned.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured, then the full captured amount returned.</summary>
    Refunded = 4,

    /// <summary>Cancelled before capture; any hold was released.</summary>
    Cancelled = 5,

    /// <summary>A payment attempt failed (e.g. card declined).</summary>
    Failed = 6
}
