using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the PayPal <c>invoice_id</c> carried on an order's charges. It embeds the eShop order id (for
/// readability/traceability) plus a unique suffix so it never collides across in-memory restarts — PayPal
/// rejects a reused invoice id. The value is stored on the <c>Payment</c> so reconciliation can match it.
/// </summary>
public static class OrderInvoice
{
    private const string Prefix = "eshop-order-";

    public static string New(int orderId) => $"{Prefix}{orderId}-{Guid.NewGuid():N}";
}
