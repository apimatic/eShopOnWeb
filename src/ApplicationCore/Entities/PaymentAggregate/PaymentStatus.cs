namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement for an <see cref="OrderAggregate.Order"/>.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured at fulfilment; nothing refunded.</summary>
    Captured = 2,

    /// <summary>Captured funds partly returned to the shopper.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured funds fully returned to the shopper.</summary>
    Refunded = 4,

    /// <summary>Authorization released before capture; no money ever moved.</summary>
    Voided = 5,

    /// <summary>A payment attempt failed and no hold survives.</summary>
    Failed = 6
}
