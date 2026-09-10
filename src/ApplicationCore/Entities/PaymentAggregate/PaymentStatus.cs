namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement state of an order's payment. This models what PayPal owns
/// (a hold, a capture, refunds) as a small state machine the operator flows drive.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no hold has been put on any money yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total is held (authorized) on the card. No money has moved.</summary>
    Authorized = 1,

    /// <summary>The held money has been taken (captured) at fulfilment.</summary>
    Captured = 2,

    /// <summary>The hold was released before fulfilment; no money ever moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been returned to the shopper.</summary>
    PartiallyRefunded = 4,

    /// <summary>The whole captured amount has been returned to the shopper.</summary>
    Refunded = 5
}
