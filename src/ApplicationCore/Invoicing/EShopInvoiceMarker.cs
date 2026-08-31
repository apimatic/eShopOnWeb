namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Markers this application stamps onto the invoices it raises so they can be told apart from the other
/// activity that shares the provider account. The provider's list endpoint returns neither the invoice
/// number nor merchant-defined fields, so the customer-id marker is what identifies our invoices there.
/// </summary>
public static class EShopInvoiceMarker
{
    /// <summary>Prefix placed on the invoice number of every bill this app raises.</summary>
    public const string InvoiceNumberPrefix = "ESHOP-";

    /// <summary>Prefix placed on the provider's MerchantCustomerId; the one "mine" signal the list returns.</summary>
    public const string CustomerIdPrefix = "eShopOnWeb:";

    /// <summary>Build the MerchantCustomerId for a given shopper.</summary>
    public static string CustomerIdFor(string buyerId) => CustomerIdPrefix + buyerId;

    /// <summary>True when a provider MerchantCustomerId was stamped by this application.</summary>
    public static bool IsEShopCustomerId(string? merchantCustomerId) =>
        merchantCustomerId is not null && merchantCustomerId.StartsWith(CustomerIdPrefix, System.StringComparison.Ordinal);
}
