namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle of an order once payment is part of the picture. Additive to the original
/// one-time-commerce flow: an order now starts awaiting payment and moves through the money-movement
/// states an operator drives.
/// </summary>
public enum OrderStatus
{
    /// <summary>Placed but not yet paid. No hold exists on the buyer's funds.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Fulfilled and captured: the money has been taken.</summary>
    Paid = 2,

    /// <summary>Cancelled before fulfilment: the hold was released and no money moved.</summary>
    Cancelled = 3
}
