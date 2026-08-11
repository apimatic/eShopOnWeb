namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of the money movement attached to an <see cref="Order"/>.
/// An order with no <see cref="Order.Payment"/> is implicitly awaiting payment.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds have been held (authorized) with PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>The authorization has been captured; money has moved to the merchant.</summary>
    Captured = 2,

    /// <summary>The authorization was voided before capture; no money moved.</summary>
    Voided = 3,

    /// <summary>The full captured amount has been refunded to the payer.</summary>
    Refunded = 4,

    /// <summary>Part of the captured amount has been refunded to the payer.</summary>
    PartiallyRefunded = 5
}
