using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the unique <c>invoice_id</c> we stamp on the PayPal authorization, capture and refund
/// for an order, so reconciliation can line PayPal's records up against eShop orders. PayPal
/// rejects duplicate invoice ids per merchant, so the reference includes a unique suffix while
/// still carrying the eShop order id.
/// </summary>
public static class OrderInvoiceReference
{
    private const string Prefix = "eshop-order-";

    /// <summary>A fresh, unique invoice reference for an order (used once per authorization).</summary>
    public static string New(int orderId) => $"{Prefix}{orderId}-{Guid.NewGuid():N}";

    /// <summary>Parse the eShop order id back out of a PayPal invoice_id, if it is one of ours.</summary>
    public static int? TryParseOrderId(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId) || !invoiceId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = invoiceId.Substring(Prefix.Length);
        var dash = rest.IndexOf('-');
        var idPart = dash >= 0 ? rest.Substring(0, dash) : rest;
        return int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId)
            ? orderId
            : null;
    }
}
