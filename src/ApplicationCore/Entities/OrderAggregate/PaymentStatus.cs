namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of the money attached to an order, driven by PayPal
/// authorization/capture/refund operations.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds are on hold (authorized) but not taken yet.</summary>
    Authorized = 0,

    /// <summary>The hold was released (cancel/void) or expired; no money moved.</summary>
    AuthorizationReleased = 1,

    /// <summary>Full authorized amount was captured (money taken at fulfilment).</summary>
    Captured = 2,

    /// <summary>Some, but not all, of the captured amount was refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The entire captured amount was refunded.</summary>
    Refunded = 4
}
