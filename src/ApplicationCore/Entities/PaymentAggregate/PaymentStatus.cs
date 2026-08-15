namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// State of the money movement PayPal owns for an order's payment.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized = 0,

    /// <summary>Funds captured (taken) at fulfilment.</summary>
    Captured = 1,

    /// <summary>Authorization released before capture; no money moved.</summary>
    Voided = 2,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 3,

    /// <summary>Captured payment partially refunded.</summary>
    PartiallyRefunded = 4
}
