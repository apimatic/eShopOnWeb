namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment lifecycle state of an <see cref="Order"/>. An order is created
/// <see cref="AwaitingPayment"/>; a successful authorization holds the funds
/// (<see cref="Authorized"/>); fulfilment captures them (<see cref="Captured"/>);
/// a return refunds them (<see cref="PartiallyRefunded"/> / <see cref="Refunded"/>);
/// a pre-fulfilment cancellation releases the hold (<see cref="Cancelled"/>).
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Cancelled = 5
}
