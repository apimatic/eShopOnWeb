namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// State of the money movement for an order's payment, mirroring what PayPal owns.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds authorized (held) but not captured.</summary>
    Authorized = 0,

    /// <summary>Funds captured (taken from the shopper).</summary>
    Captured = 1,

    /// <summary>Authorization voided before capture; hold released.</summary>
    Voided = 2,

    /// <summary>Capture fully refunded.</summary>
    Refunded = 3,

    /// <summary>Capture partially refunded.</summary>
    PartiallyRefunded = 4
}
