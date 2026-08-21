namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an <see cref="OrderPayment"/>. This mirrors what happens on the
/// PayPal side: the money is first held (authorized), then taken (captured) at fulfilment,
/// released on cancel (voided), or returned after fulfilment (refunded).
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no hold on the money yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Money is held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Money has been taken (captured) at fulfilment.</summary>
    Captured = 2,

    /// <summary>The hold was released before fulfilment; no money moved.</summary>
    Voided = 3,

    /// <summary>The captured payment has been refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>The captured payment has been refunded in full.</summary>
    Refunded = 5,

    /// <summary>PayPal returned a challenge that needs the shopper to approve in a browser.</summary>
    ActionRequired = 6,

    /// <summary>The payment attempt was declined or otherwise failed.</summary>
    Failed = 7
}
