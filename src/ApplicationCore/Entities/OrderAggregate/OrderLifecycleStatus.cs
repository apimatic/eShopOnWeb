namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order sits between being placed and the money finally settling.
/// Orders created before any payment was taken (e.g. by the storefront checkout) start at
/// <see cref="AwaitingPayment"/>, which is accurate: no money has moved.
/// </summary>
public enum OrderLifecycleStatus
{
    AwaitingPayment = 0,

    /// <summary>Funds are held at the payment processor but have not been taken.</summary>
    Authorized = 1,

    /// <summary>Fulfilled, and the held funds have been captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any hold was released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled, then part of the captured amount was returned.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled, then the whole captured amount was returned.</summary>
    Refunded = 5
}
