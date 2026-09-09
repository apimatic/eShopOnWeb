namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where the money is, from eShop's point of view. Mirrors the PayPal resource lifecycle:
/// authorization -> capture -> refund, plus the void (release) branch.
/// </summary>
public enum PaymentStatus
{
    /// <summary>An authorization (hold) exists; money is reserved but not taken.</summary>
    Authorized = 0,

    /// <summary>The authorization was captured; money has been taken.</summary>
    Captured = 1,

    /// <summary>The authorization was voided before capture; the hold was released.</summary>
    Voided = 2,

    /// <summary>A capture was refunded in part; some captured money has been returned.</summary>
    PartiallyRefunded = 3,

    /// <summary>A capture was fully refunded; all captured money has been returned.</summary>
    Refunded = 4
}
