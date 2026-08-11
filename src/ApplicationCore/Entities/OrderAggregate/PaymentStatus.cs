namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an order's payment, driven by the money movement at PayPal.
/// An order with no <see cref="Payment"/> yet is implicitly awaiting payment.
/// </summary>
public enum PaymentStatus
{
    /// <summary>The order has been placed but no hold has been taken.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured in full (order fulfilled).</summary>
    Captured = 2,

    /// <summary>Some — but not all — of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The whole captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before capture (order cancelled); no money moved.</summary>
    Voided = 5,

    /// <summary>The payment attempt failed.</summary>
    Failed = 6
}
