using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// eShop supplies the invoice number when raising a bill; the provider adopts it verbatim as the
/// bill's identifier. Every eShop-raised bill therefore carries a recognisable marker prefix, which
/// lets operator reconciliation tell eShop's bills apart from the other bills that share the
/// provider account — even for a bill the provider knows about that eShop no longer has a local
/// record of.
/// </summary>
public static class EShopInvoiceNumber
{
    public const string Prefix = "ESHOP-";

    /// <summary>
    /// Build a unique, provider-safe invoice number for an order. Contains only letters, digits and
    /// hyphens. The embedded order id keeps it human-readable; the random suffix keeps it unique.
    /// </summary>
    public static string Create(int orderId)
    {
        var token = Guid.NewGuid().ToString("N").Substring(0, 12);
        return $"{Prefix}{orderId}-{token}";
    }

    /// <summary>Whether an identifier looks like a bill this application raised.</summary>
    public static bool IsEShopInvoice(string? providerInvoiceId) =>
        providerInvoiceId is not null &&
        providerInvoiceId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}
