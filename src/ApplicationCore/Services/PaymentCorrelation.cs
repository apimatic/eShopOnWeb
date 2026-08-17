using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The distinctive token carried on PayPal's <c>custom_id</c> so reconciliation can line a PayPal
/// transaction back to an eShop order. A bare integer id would false-match unrelated transactions in a
/// shared account, so the order id is namespaced.
/// </summary>
public static class PaymentCorrelation
{
    private const string Prefix = "eshop-order-";

    public static string OrderToken(int orderId) => $"{Prefix}{orderId}";

    public static bool TryParseOrderId(string? token, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(token.AsSpan(Prefix.Length), out orderId);
    }
}
