namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order's payment. This is additive to the existing order model and
/// mirrors the money movement performed against PayPal.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>PayPal is holding (authorizing) the funds, nothing captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>Authorization voided before fulfilment; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured funds partially returned.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured funds fully returned.</summary>
    Refunded = 5
}
