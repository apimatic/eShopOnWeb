namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Application-level invoicing policy that is not the provider's to decide. eShop prices its catalog
/// without a currency, so the billing currency is fixed here (from configuration) rather than being
/// picked per call.
/// </summary>
public class InvoicingSettings
{
    public string Currency { get; set; } = "USD";
}
