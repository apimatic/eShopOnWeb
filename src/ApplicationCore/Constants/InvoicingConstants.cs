namespace Microsoft.eShopWeb.ApplicationCore.Constants;

/// <summary>
/// Fixed facts about how eShop raises bills on this provider account.
/// </summary>
public static class InvoicingConstants
{
    /// <summary>
    /// This provider account bills in USD; every bill eShop raises uses this currency rather than one
    /// picked per call.
    /// </summary>
    public const string Currency = "USD";

    /// <summary>
    /// Prefix stamped onto the invoice number of every bill eShop raises, for human-readable
    /// identification on the provider's invoice.
    /// </summary>
    public const string InvoiceNumberPrefix = "ESHOP-";

    /// <summary>
    /// Prefix stamped onto the customer reference (the provider's <c>merchantCustomerId</c>) of every
    /// bill eShop raises. Unlike the invoice number, the customer reference is returned by the provider's
    /// list endpoint, so this is what reconciliation uses to tell eShop's bills apart from other bills on
    /// the shared provider account. The shopper's id follows the prefix.
    /// </summary>
    public const string MerchantReferencePrefix = "eShopOnWeb:";
}
