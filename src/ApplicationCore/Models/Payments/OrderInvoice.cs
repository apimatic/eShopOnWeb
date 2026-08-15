namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// The invoice-id convention that links a PayPal transaction back to an eShop order. Set as
/// <c>invoice_id</c> on the PayPal order/capture/refund and read back from Transaction Search to
/// reconcile. Includes a per-run nonce so ids stay globally unique even though the in-memory
/// database resets order ids to 1 on each restart (PayPal requires unique invoice ids per merchant).
/// </summary>
public static class OrderInvoice
{
    public const string Prefix = "eshop-order-";

    /// <summary>Builds the invoice id for an order, made unique by the supplied run nonce.</summary>
    public static string For(int orderId, string runNonce) => $"{Prefix}{orderId}-{runNonce}";

    public static int? TryGetOrderId(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId) || !invoiceId!.StartsWith(Prefix, System.StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = invoiceId.Substring(Prefix.Length);
        var dash = remainder.IndexOf('-');
        var idPart = dash >= 0 ? remainder.Substring(0, dash) : remainder;
        return int.TryParse(idPart, out var orderId) ? orderId : null;
    }
}
