namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    /// <summary>Funds are on hold with PayPal; not yet captured.</summary>
    Authorized = 0,
    /// <summary>Funds were captured at fulfilment.</summary>
    Captured = 1,
    /// <summary>Captured and partly refunded.</summary>
    PartiallyRefunded = 2,
    /// <summary>Captured and fully refunded.</summary>
    Refunded = 3,
    /// <summary>Authorization was voided on cancel; no money moved.</summary>
    Voided = 4
}
