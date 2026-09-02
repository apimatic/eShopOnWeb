namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Payment record created; authorization not yet completed.</summary>
    PendingAuthorization = 0,
    /// <summary>Funds are on hold at PayPal; not yet captured.</summary>
    Authorized = 1,
    /// <summary>Authorization was declined or could not be renewed.</summary>
    AuthorizationFailed = 2,
    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 3,
    /// <summary>Authorization voided on cancel; no money moved.</summary>
    Voided = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
