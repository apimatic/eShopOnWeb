namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>Application-level invoicing settings. The provider account bills in a single currency; it is
/// configured rather than picked per call.</summary>
public class InvoicingSettings
{
    public string Currency { get; set; } = "USD";
}
