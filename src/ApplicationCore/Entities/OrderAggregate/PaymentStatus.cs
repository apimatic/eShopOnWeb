namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The state of the money movement for a <see cref="OrderPayment"/>, mirroring what the
/// payment processor (PayPal) reports for the hold, the capture and any refunds.
/// </summary>
public enum PaymentStatus
{
    /// <summary>A payment record exists but no authorization has been placed yet.</summary>
    Pending = 0,

    /// <summary>Funds are held on the shopper's card (PayPal authorization created).</summary>
    Authorized = 1,

    /// <summary>The authorization was captured; the money has been taken.</summary>
    Captured = 2,

    /// <summary>The capture has been partially refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The capture has been fully refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided; the held funds were released.</summary>
    Voided = 5
}
