namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an order. eShopOnWeb orders originally carried no such state;
/// this drives the pay → fulfil → refund (or cancel) flow.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no hold has been taken yet.</summary>
    PendingAuthorization = 0,

    /// <summary>Funds are held (authorized) but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; the held funds have been captured (money taken).</summary>
    Captured = 2,

    /// <summary>Authorization voided before fulfilment; the hold was released and no money moved.</summary>
    Voided = 3,

    /// <summary>The captured payment has been partly returned.</summary>
    PartiallyRefunded = 4,

    /// <summary>The captured payment has been returned in full.</summary>
    Refunded = 5
}
