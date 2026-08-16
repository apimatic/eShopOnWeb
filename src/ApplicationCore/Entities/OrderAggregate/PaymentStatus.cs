namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle of the money movement attached to an <see cref="Order"/>.
/// This is the app's own view of the payment; it is derived from what PayPal reports
/// but is owned and enforced by the domain.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no hold has been put on the shopper's money yet.</summary>
    PendingAuthorization = 0,

    /// <summary>PayPal is holding the order total; funds have not been taken.</summary>
    Authorized = 1,

    /// <summary>The order was fulfilled and the held funds were captured (money taken).</summary>
    Captured = 2,

    /// <summary>The authorization was released before capture; no money ever moved.</summary>
    Voided = 3,

    /// <summary>Some — but not all — of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The whole captured amount has been refunded.</summary>
    Refunded = 5
}
