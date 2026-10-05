namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>An authorization request was sent and its outcome is not yet known.</summary>
    AuthorizationPending = 0,
    Authorized = 1,
    Declined = 2,
    /// <summary>The authorization can no longer be captured or renewed; the shopper has to pay again.</summary>
    AuthorizationExpired = 3,
    /// <summary>A capture request was sent and its outcome is not yet known.</summary>
    CapturePending = 4,
    Captured = 5,
    PartiallyRefunded = 6,
    Refunded = 7,
    /// <summary>A void request was sent and its outcome is not yet known.</summary>
    VoidPending = 8,
    Voided = 9,
    /// <summary>Created; no authorization attempted yet.</summary>
    NotStarted = 10
}
