namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order once payment is involved. An order created through the
/// payments API starts <see cref="AwaitingPayment"/> and moves through a hold
/// (<see cref="Authorized"/>) to either release (<see cref="Cancelled"/>) or
/// capture (<see cref="Fulfilled"/>), after which it can be refunded.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization) but not captured.</summary>
    Authorized = 1,

    /// <summary>Authorization voided before capture; no money ever moved.</summary>
    Cancelled = 2,

    /// <summary>Authorization captured at fulfilment; money taken.</summary>
    Fulfilled = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
