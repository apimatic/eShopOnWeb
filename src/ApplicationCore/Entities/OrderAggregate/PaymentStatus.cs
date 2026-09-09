namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The state of the money movement for a <see cref="Payment"/>, mirroring the
/// stages PayPal owns: an authorization hold, a capture, and any refunds against it.
/// </summary>
public enum PaymentStatus
{
    Authorized = 0,
    Captured = 1,
    PartiallyRefunded = 2,
    Refunded = 3,
    Voided = 4
}
