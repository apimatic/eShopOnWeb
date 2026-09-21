namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle of the money movement attached to an <see cref="Order"/>.
/// Additive to the existing order flow — an order with no payment is simply
/// <see cref="AwaitingPayment"/> until a shopper pays for it.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) with PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>The payment has been captured (money taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>The authorization was released before fulfilment; no money moved.</summary>
    Voided = 3,

    /// <summary>The captured payment has been fully refunded.</summary>
    Refunded = 4,

    /// <summary>Part of the captured payment has been refunded.</summary>
    PartiallyRefunded = 5,

    /// <summary>The authorization or capture could not be completed.</summary>
    Failed = 6
}
