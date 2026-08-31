namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Created locally, no hold placed with PayPal yet.</summary>
    AwaitingPayment = 0,
    /// <summary>Funds are on hold with PayPal (authorization created).</summary>
    Authorized = 1,
    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 2,
    /// <summary>Hold released without any money moving (order cancelled).</summary>
    Voided = 3,
    /// <summary>Captured and partly refunded.</summary>
    PartiallyRefunded = 4,
    /// <summary>Captured and fully refunded.</summary>
    Refunded = 5
}
