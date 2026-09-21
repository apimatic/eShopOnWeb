namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The fulfilment/settlement state of an <see cref="OrderPayment"/>. This is the payment lifecycle that
/// sits alongside the existing order — the order itself carries no payment state.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money has been held yet (awaiting payment).</summary>
    PendingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created). No money has moved.</summary>
    Authorized = 1,

    /// <summary>The held funds have been captured at fulfilment. Money has moved to the merchant.</summary>
    Fulfilled = 2,

    /// <summary>The hold was released before fulfilment (voided). No money ever moved.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured amount has been refunded; further partial refunds may remain possible.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>Authorization was declined or otherwise could not be completed.</summary>
    Failed = 6
}
