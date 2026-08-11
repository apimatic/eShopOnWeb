namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of an order's payment, from the moment an order is placed
/// through authorization (hold), capture (money taken at fulfilment), and any
/// releases or refunds that follow.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Money has been taken (captured) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Some — but not all — of the captured money has been returned.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been returned.</summary>
    Refunded = 4,

    /// <summary>The hold was released before fulfilment; no money ever moved.</summary>
    Cancelled = 5
}
