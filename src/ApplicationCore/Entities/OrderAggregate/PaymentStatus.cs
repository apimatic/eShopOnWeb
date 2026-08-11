namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// State of the money for a <see cref="Payment"/>, mirroring what PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>No hold placed yet.</summary>
    Pending = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured (taken).</summary>
    Captured = 2,

    /// <summary>Captured funds partially returned to the shopper.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured funds fully returned to the shopper.</summary>
    Refunded = 4,

    /// <summary>Authorization released before capture; no money moved.</summary>
    Voided = 5
}
