namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of the money movement attached to an <see cref="Order"/>.
/// This is additive to the classic eShopOnWeb order flow: an order that is never paid
/// simply stays in <see cref="PendingPayment"/>.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed, no authorization hold taken yet.</summary>
    PendingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Payment captured — money has actually moved to the merchant.</summary>
    Paid = 2,

    /// <summary>Authorization voided before capture — held funds released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment partially refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 5
}
