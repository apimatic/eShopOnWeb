namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of an order's payment as it moves through the PayPal hold / capture / refund flow.
/// This is additive state that lives alongside the existing <see cref="OrderAggregate.Order"/> aggregate
/// rather than modifying it.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization) but not captured.</summary>
    Authorized = 1,

    /// <summary>PayPal declined the card / the authorization could not be created.</summary>
    AuthorizationFailed = 2,

    /// <summary>The authorization was captured at fulfilment; money has moved.</summary>
    Captured = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>The hold was released before capture; no money moved.</summary>
    Cancelled = 6
}
