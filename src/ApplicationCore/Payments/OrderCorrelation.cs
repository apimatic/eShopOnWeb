using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Correlation value carried on PayPal purchase units / refunds (invoice_id) so PayPal's own
/// transaction records can be lined up against eShop orders during reconciliation.
/// </summary>
public static class OrderCorrelation
{
    public const string Prefix = "ESHOP-";

    public static string ForOrder(int orderId) => $"{Prefix}{orderId}";

    /// <summary>
    /// A globally-unique invoice id that still encodes the order id. PayPal requires invoice ids to
    /// be unique per transaction, so a stable "ESHOP-{id}" cannot be reused across attempts or runs;
    /// the trailing token guarantees uniqueness while <see cref="TryParseOrderId"/> still recovers the id.
    /// </summary>
    public static string UniqueForOrder(int orderId, string? suffix = null) =>
        suffix is null
            ? $"{Prefix}{orderId}-{Guid.NewGuid():N}"
            : $"{Prefix}{orderId}-{suffix}-{Guid.NewGuid():N}";

    /// <summary>Extract the eShop order id from a PayPal invoice_id, if it carries our prefix.</summary>
    public static int? TryParseOrderId(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId) || !invoiceId.StartsWith(Prefix))
            return null;

        var rest = invoiceId.Substring(Prefix.Length);
        // Refunds may append a suffix (e.g. ESHOP-12-R1); take the leading digits.
        var end = 0;
        while (end < rest.Length && char.IsDigit(rest[end])) end++;
        if (end == 0) return null;

        return int.TryParse(rest.Substring(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }
}
