namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// State of the money movement PayPal owns for a <see cref="Payment"/>: a hold (Authorized),
/// the taken money (Captured), a released hold (Voided) or a returned capture (Refunded).
/// </summary>
public enum PaymentStatus
{
    Authorized = 0,
    Captured = 1,
    Voided = 2,
    PartiallyRefunded = 3,
    Refunded = 4
}
