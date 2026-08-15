namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an order. An order begins <see cref="AwaitingPayment"/>; a
/// successful hold makes it <see cref="Authorized"/>; fulfilment captures it to <see cref="Captured"/>;
/// a pre-fulfilment cancel voids it to <see cref="Cancelled"/>; refunds after capture move it through
/// <see cref="PartiallyRefunded"/> to <see cref="Refunded"/>.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Captured = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Failed = 6
}
