namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an order, additive to the existing order model.
/// PayPal owns the money movement behind each transition (authorize → capture → refund/void).
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>PayPal is holding the order total (an authorization). No money has been taken.</summary>
    Authorized = 1,

    /// <summary>The authorization was captured at fulfilment; the money has been taken.</summary>
    Captured = 2,

    /// <summary>The capture has been refunded in part; further refunds up to the captured amount remain possible.</summary>
    PartiallyRefunded = 3,

    /// <summary>The capture has been fully refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before fulfilment; the held funds were released.</summary>
    Cancelled = 5,

    /// <summary>PayPal answered with a challenge that needs shopper approval in a browser — stopped and reported.</summary>
    RequiresApproval = 6,

    /// <summary>PayPal declined the payment.</summary>
    Failed = 7
}
