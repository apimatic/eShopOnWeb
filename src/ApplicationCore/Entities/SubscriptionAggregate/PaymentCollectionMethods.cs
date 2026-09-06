namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maxio collection methods.
/// </summary>
public static class PaymentCollectionMethods
{
    /// <summary>Maxio charges a stored payment profile automatically.</summary>
    public const string Automatic = "automatic";

    /// <summary>Maxio invoices the customer; no stored payment profile is needed.</summary>
    public const string Remittance = "remittance";

    public const string Prepaid = "prepaid";

    public const string Invoice = "invoice";
}
