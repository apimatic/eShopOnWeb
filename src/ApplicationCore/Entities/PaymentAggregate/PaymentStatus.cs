namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Created locally, no successful authorization yet.</summary>
    Pending = 0,
    /// <summary>Funds are on hold with the payment provider.</summary>
    Authorized = 1,
    /// <summary>The provider declined the authorization.</summary>
    AuthorizationFailed = 2,
    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    /// <summary>Authorization voided; held funds released.</summary>
    Voided = 6,
    /// <summary>The authorization went stale and cannot be renewed; the shopper must pay again.</summary>
    RequiresNewAuthorization = 7
}
