namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of an order's payment, from placement through fulfilment and returns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no hold has been taken yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Money has been taken (PayPal capture completed) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Cancelled before fulfilment; the authorization was voided and no money moved.</summary>
    Cancelled = 3,

    /// <summary>The captured payment has been refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>The captured payment has been fully refunded.</summary>
    Refunded = 5,

    /// <summary>The authorization attempt was declined or failed.</summary>
    Failed = 6
}
