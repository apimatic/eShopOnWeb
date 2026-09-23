namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of an order's payment. An order with no <see cref="OrderPayment"/> row is implicitly
/// <see cref="AwaitingPayment"/>.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>No money has been held. The order is awaiting payment.</summary>
    AwaitingPayment = 0,

    /// <summary>A payment row has been claimed and the authorization is in flight at PayPal.</summary>
    Authorizing = 1,

    /// <summary>PayPal is holding the funds (authorized) but nothing has been captured.</summary>
    Authorized = 2,

    /// <summary>The authorized funds have been captured (the order was fulfilled).</summary>
    Fulfilled = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>The hold was released before capture (the order was cancelled).</summary>
    Cancelled = 6,

    /// <summary>The authorization or capture failed at PayPal.</summary>
    Failed = 7
}
