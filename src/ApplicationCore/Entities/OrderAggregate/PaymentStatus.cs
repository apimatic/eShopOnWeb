namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    /// <summary>Funds are on hold with the payment provider.</summary>
    Authorized = 0,

    /// <summary>Funds were captured (taken) at fulfilment.</summary>
    Captured = 1,

    /// <summary>Capture was partly refunded.</summary>
    PartiallyRefunded = 2,

    /// <summary>Capture was refunded in full.</summary>
    Refunded = 3,

    /// <summary>Authorization was voided; held funds were released without money moving.</summary>
    Voided = 4,

    /// <summary>The authorization attempt was declined or failed.</summary>
    Failed = 5
}
