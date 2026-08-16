using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Stable references tying a PayPal transaction back to an eShop order. The invoice id embeds the
/// order id (for humans / fallback parsing) plus the order's globally-unique payment-intent id (so
/// it never collides across app runs). custom_id carries the bare order id.
/// </summary>
public static class PaymentReference
{
    public const string InvoicePrefix = "ESHOP-";

    public static string InvoiceId(int orderId, string paymentIntentId)
    {
        var shortIntent = paymentIntentId.Length > 12 ? paymentIntentId.Substring(0, 12) : paymentIntentId;
        return $"{InvoicePrefix}{orderId}-{shortIntent}";
    }

    /// <summary>Parses the order id out of an "ESHOP-{orderId}-{intent}" invoice id.</summary>
    public static bool TryParseOrderId(string? invoiceId, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrEmpty(invoiceId) || !invoiceId.StartsWith(InvoicePrefix, StringComparison.Ordinal))
        {
            return false;
        }
        var rest = invoiceId.Substring(InvoicePrefix.Length);
        var dash = rest.IndexOf('-');
        var idPart = dash >= 0 ? rest.Substring(0, dash) : rest;
        return int.TryParse(idPart, out orderId);
    }
}
