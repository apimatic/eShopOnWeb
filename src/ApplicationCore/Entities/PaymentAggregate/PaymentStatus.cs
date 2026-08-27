namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Payment record created; authorization not yet confirmed.</summary>
    PendingAuthorization = 0,

    /// <summary>Funds are on hold (authorized) with the payment provider.</summary>
    Authorized = 1,

    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>Authorization voided; held funds released. No money moved.</summary>
    Voided = 3,

    /// <summary>The provider rejected the authorization.</summary>
    Failed = 4
}
