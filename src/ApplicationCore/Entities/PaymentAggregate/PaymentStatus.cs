namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an order, mirroring the money movement performed at PayPal.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>PayPal is holding the funds (authorization), but nothing has been captured.</summary>
    Authorized = 1,

    /// <summary>The authorization has been captured at fulfilment — the money has moved.</summary>
    Captured = 2,

    /// <summary>Some of the captured amount has been refunded, but not all of it.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before capture — no money ever moved.</summary>
    Voided = 5,

    /// <summary>The last payment operation failed (e.g. the card was declined).</summary>
    Failed = 6
}
