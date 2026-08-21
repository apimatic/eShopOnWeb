namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement for an order. The order is placed <see cref="AwaitingPayment"/>,
/// the hold is placed at pay time (<see cref="Authorized"/>), the money is taken at fulfilment
/// (<see cref="Captured"/>), released before fulfilment (<see cref="Voided"/>), or returned after
/// fulfilment (<see cref="PartiallyRefunded"/> / <see cref="Refunded"/>).
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Voided = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
