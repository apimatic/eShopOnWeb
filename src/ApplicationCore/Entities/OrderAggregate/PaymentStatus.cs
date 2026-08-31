namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    /// <summary>Created locally, no authorization obtained from PayPal yet.</summary>
    Pending = 0,
    /// <summary>Funds are on hold at PayPal (authorization created).</summary>
    Authorized = 1,
    /// <summary>The last authorization attempt was declined or failed.</summary>
    AuthorizationFailed = 2,
    /// <summary>The authorization was voided; held funds were released.</summary>
    Voided = 3,
    /// <summary>Funds were captured at fulfilment.</summary>
    Captured = 4,
    /// <summary>Captured and partly refunded.</summary>
    PartiallyRefunded = 5,
    /// <summary>Captured and fully refunded.</summary>
    Refunded = 6
}
