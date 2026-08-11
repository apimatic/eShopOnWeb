namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of the money movement for an order, mirroring the states PayPal owns.
/// An order with no <see cref="Payment"/> row at all is implicitly "awaiting payment".
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds have been taken (the authorization was captured at fulfilment).</summary>
    Captured = 2,

    /// <summary>A capture that has been refunded in part.</summary>
    PartiallyRefunded = 3,

    /// <summary>A capture that has been fully refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before fulfilment; no money ever moved.</summary>
    Voided = 5
}
