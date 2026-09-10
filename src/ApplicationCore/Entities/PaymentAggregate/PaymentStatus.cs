namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The state of the money movement for a <see cref="Payment"/>, mirroring the PayPal-owned state.
/// </summary>
public enum PaymentStatus
{
    /// <summary>A PayPal order has been created but no hold exists yet.</summary>
    Created = 0,

    /// <summary>Funds are held (authorized) but not yet captured.</summary>
    Authorized = 1,

    /// <summary>The authorization was captured; money has been taken.</summary>
    Captured = 2,

    /// <summary>Some, but not all, of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The whole captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before capture; the hold was released.</summary>
    Voided = 5,

    /// <summary>The payment attempt failed.</summary>
    Failed = 6
}
