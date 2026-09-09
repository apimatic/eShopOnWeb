namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Maps an eShop order id to the invoice reference we hand PayPal (as invoice_id / custom_id), so
/// reconciliation can line a PayPal transaction back up with the order that produced it.
///
/// PayPal requires invoice ids to be unique per merchant account across all time. Because the app
/// runs on the in-memory database — where order ids restart from 1 on every run — a bare
/// <c>ESHOP-{orderId}</c> would collide with a previous run. A per-run token is appended to keep the
/// invoice id unique while leaving the order id parseable for reconciliation.
/// </summary>
public static class OrderInvoiceReference
{
    private const string Prefix = "ESHOP-";

    public static string For(int orderId, string runToken) => $"{Prefix}{orderId}-{runToken}";

    public static bool TryParse(string? invoiceId, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrEmpty(invoiceId) || !invoiceId.StartsWith(Prefix))
            return false;

        var rest = invoiceId.Substring(Prefix.Length);
        var dash = rest.IndexOf('-');
        var orderPart = dash >= 0 ? rest.Substring(0, dash) : rest;
        return int.TryParse(orderPart, out orderId);
    }
}
