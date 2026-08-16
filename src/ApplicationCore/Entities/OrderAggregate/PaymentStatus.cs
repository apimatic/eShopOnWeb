namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Status of the PayPal payment backing an order. Mirrors the money-movement steps that PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>A payment record exists but the authorization has not completed.</summary>
    Pending = 0,

    /// <summary>Funds are authorized (held) on the card. No money has been taken.</summary>
    Authorized = 1,

    /// <summary>The authorization was captured; money has been taken from the shopper.</summary>
    Captured = 2,

    /// <summary>The authorization was voided; the hold was released.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The whole captured amount has been refunded.</summary>
    Refunded = 5
}
