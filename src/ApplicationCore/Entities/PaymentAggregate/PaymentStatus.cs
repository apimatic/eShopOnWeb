namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment. Additive to the existing order model — an order that has
/// never been paid simply has no <see cref="OrderPayment"/>, though this integration creates one (in
/// <see cref="PaymentStatus.AwaitingPayment"/>) whenever an order is placed through the payment API.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held or moved yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created) but not captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; funds captured from the authorization.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the authorization hold was released (voided).</summary>
    Cancelled = 3,

    /// <summary>The captured payment was fully refunded.</summary>
    Refunded = 4,

    /// <summary>The captured payment was refunded in part.</summary>
    PartiallyRefunded = 5,

    /// <summary>Authorization was attempted but declined/failed; no money is held.</summary>
    Failed = 6
}
