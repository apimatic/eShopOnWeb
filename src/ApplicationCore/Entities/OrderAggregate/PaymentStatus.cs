namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Money-movement lifecycle of a <see cref="Payment"/>. Distinct from <see cref="OrderStatus"/>
/// (which tracks fulfilment): a payment can be Captured while its refunds evolve independently.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 0,

    /// <summary>Funds have been taken (captured) from the shopper.</summary>
    Captured = 1,

    /// <summary>The authorization was voided before capture; no money moved.</summary>
    Voided = 2,

    /// <summary>Part of the captured amount has been returned to the shopper.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been returned to the shopper.</summary>
    Refunded = 4,
}
