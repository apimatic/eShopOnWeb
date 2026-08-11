namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The state of the money movement for a <see cref="Payment"/>, mirroring what PayPal owns.
/// </summary>
public enum PaymentState
{
    /// <summary>A hold has been placed on the buyer's funds; nothing has been taken.</summary>
    Authorized = 0,

    /// <summary>The held funds have been captured (taken) in full.</summary>
    Captured = 1,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 2,

    /// <summary>The whole captured amount has been refunded.</summary>
    Refunded = 3,

    /// <summary>The authorization was voided before capture; no money moved.</summary>
    Voided = 4
}
