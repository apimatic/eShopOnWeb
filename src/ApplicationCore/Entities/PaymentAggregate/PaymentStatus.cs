namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Created locally; authorization not yet confirmed by PayPal.</summary>
    PendingAuthorization = 0,
    /// <summary>Funds are on hold at PayPal (authorized, not captured).</summary>
    Authorized = 1,
    /// <summary>Hold released without any money moving (order cancelled).</summary>
    Voided = 2,
    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 3,
    /// <summary>Captured, with one or more partial refunds below the captured amount.</summary>
    PartiallyRefunded = 4,
    /// <summary>Captured amount fully refunded.</summary>
    Refunded = 5,
    /// <summary>PayPal declined the authorization.</summary>
    AuthorizationFailed = 6
}
