namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment, mirroring the money movement that
/// happens on PayPal's side: a hold is placed at authorization, the money is taken
/// at capture (fulfilment) and given back on a void (cancel) or refund (return).
/// </summary>
public enum PaymentStatus
{
    /// <summary>The order has been placed but no PayPal hold exists yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) on PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds have been taken (captured) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded to the shopper.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded to the shopper.</summary>
    Refunded = 4,

    /// <summary>The hold was released before fulfilment; no money ever moved.</summary>
    Voided = 5,

    /// <summary>PayPal declined or failed the authorization attempt.</summary>
    Failed = 6
}
