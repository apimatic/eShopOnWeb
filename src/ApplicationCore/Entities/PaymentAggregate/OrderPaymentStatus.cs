namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement that backs an <see cref="OrderAggregate.Order"/>.
/// The order itself is created in <see cref="AwaitingPayment"/>; every other state is
/// driven by a PayPal interaction (authorize/capture/void/refund).
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Some – but not all – of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>Authorization released before capture; no money ever moved.</summary>
    Canceled = 5,

    /// <summary>The authorization attempt was declined or otherwise failed.</summary>
    Failed = 6
}
