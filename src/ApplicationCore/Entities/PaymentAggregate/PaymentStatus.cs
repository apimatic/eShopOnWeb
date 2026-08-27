namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Created locally, no hold placed with PayPal yet.</summary>
    Pending = 0,
    /// <summary>Funds are on hold with PayPal (authorization created).</summary>
    Authorized = 1,
    /// <summary>Hold released without any money moving.</summary>
    Voided = 2,
    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 3,
    /// <summary>Captured and partly refunded.</summary>
    PartiallyRefunded = 4,
    /// <summary>Captured and fully refunded.</summary>
    Refunded = 5
}
