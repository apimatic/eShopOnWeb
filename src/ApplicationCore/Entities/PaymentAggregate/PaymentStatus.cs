namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The fulfilment/money state of an <see cref="OrderPayment"/>. Additive to the existing order flow:
/// an order placed through the API starts <see cref="PendingPayment"/> and moves forward as the shopper
/// pays and an operator fulfils, cancels or refunds it.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, awaiting payment. No money held.</summary>
    PendingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>Authorization released before capture; no money moved.</summary>
    Cancelled = 5,

    /// <summary>Payment attempt failed.</summary>
    Failed = 6
}
