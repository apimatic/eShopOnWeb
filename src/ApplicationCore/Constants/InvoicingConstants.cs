namespace Microsoft.eShopWeb.ApplicationCore.Constants;

public static class InvoicingConstants
{
    /// <summary>
    /// Prefix stamped onto the provider invoice number for every bill eShop raises. The provider
    /// account is shared and carries bills that are not this application's; this marker is a
    /// secondary signal (alongside eShop's own records) that lets the reconciliation report make
    /// plain which provider bills originated from eShop.
    /// </summary>
    public const string EShopInvoiceNumberPrefix = "ESHOP-";

    /// <summary>
    /// This provider account bills in USD; every bill eShop raises uses this currency rather than
    /// one picked per call. eShopOnWeb prices its catalog without recording a currency.
    /// </summary>
    public const string Currency = "USD";
}
