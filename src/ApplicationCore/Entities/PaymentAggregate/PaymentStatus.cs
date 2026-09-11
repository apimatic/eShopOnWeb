namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement for an order. An order with no <see cref="Payment"/>
/// row is implicitly "AwaitingPayment"; every other state is captured here.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Payment record created but the authorization has not completed yet.</summary>
    Pending = 0,

    /// <summary>Funds are held with PayPal (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>The hold was released before fulfilment; no money moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>The authorization attempt failed (e.g. the card was declined).</summary>
    Failed = 6
}
