namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Funds are authorized (held) with PayPal but not yet captured.</summary>
    Authorized = 0,

    /// <summary>Funds have been captured at fulfilment.</summary>
    Captured = 1,

    /// <summary>The authorization was voided (order cancelled); no money moved.</summary>
    Voided = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The captured amount has been refunded in full.</summary>
    Refunded = 4,

    /// <summary>The authorization went stale and could not be renewed; the shopper must pay again.</summary>
    AuthorizationExpired = 5
}
