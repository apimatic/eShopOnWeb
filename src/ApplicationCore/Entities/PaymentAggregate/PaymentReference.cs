using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Builds and parses the invoice reference carried on every PayPal transaction for an order.
/// This is the key reconciliation lines PayPal's records up against eShop orders by.
/// PayPal enforces that <c>invoice_id</c> is unique per merchant forever, so the reference embeds
/// the payment's unique seed as well as the order id (which alone would repeat across in-memory
/// restarts and a reused sandbox account).
/// </summary>
public static class PaymentReference
{
    private const string Prefix = "ESHOP-ORDER-";

    /// <summary>Builds the globally-unique, parseable invoice reference for a payment.</summary>
    public static string For(Payment payment) => For(payment.OrderId, payment.IdempotencySeed);

    public static string For(int orderId, Guid seed) =>
        $"{Prefix}{orderId.ToString(CultureInfo.InvariantCulture)}-{seed:N}";

    /// <summary>Extracts the eShop order id from an invoice reference, or null if it is not one of ours.</summary>
    public static int? TryGetOrderId(string? invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId) || !invoiceId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = invoiceId.Substring(Prefix.Length);
        var dash = rest.IndexOf('-');
        var idPart = dash >= 0 ? rest.Substring(0, dash) : rest;

        return int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }
}
