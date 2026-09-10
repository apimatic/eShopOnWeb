namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The state of the money movement backing an order, mirroring what PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>The PayPal authorization (hold) is in place; no money has moved.</summary>
    Authorized = 0,

    /// <summary>The authorization was captured; funds have been taken.</summary>
    Captured = 1,

    /// <summary>The authorization was voided before capture; the hold was released.</summary>
    Voided = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4
}
