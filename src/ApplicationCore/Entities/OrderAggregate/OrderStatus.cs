namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>Created but not yet paid. Awaiting payment.</summary>
    PendingPayment = 0,

    /// <summary>An authorization (hold) is in place; the money has not been taken yet.</summary>
    Paid = 1,

    /// <summary>The operator fulfilled the order and the capture settled; money has been taken.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled and fully refunded.</summary>
    Refunded = 4
}