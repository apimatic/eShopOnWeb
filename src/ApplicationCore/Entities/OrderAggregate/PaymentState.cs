namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The state of the money for an order, as reflected from PayPal.
/// </summary>
public enum PaymentState
{
    /// <summary>No payment attempted yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (authorization CREATED) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured in full, no refunds.</summary>
    Captured = 2,

    /// <summary>Captured, then partially refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured, then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Authorization released (voided); no money moved.</summary>
    Voided = 5
}
