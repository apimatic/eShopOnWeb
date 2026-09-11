using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds and parses the reference that ties a provider transaction back to an eShop order.
/// The reference is stored on the provider side (as invoice id / custom id) so reconciliation can
/// line the two systems up.
///
/// The reference (and the idempotency keys) include a per-process <see cref="RunId"/>. Within one
/// run the reference for an order is stable, so a retried request is idempotent; across runs it
/// differs, so a fresh run never collides with a prior run's provider records — important because
/// the sandbox merchant account persists across runs and requires globally-unique invoice ids,
/// while the in-memory database restarts order ids from 1 each run.
/// </summary>
public static class PaymentReference
{
    public const string Prefix = "eshop-order-";

    /// <summary>Short token unique to this process/run.</summary>
    public static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    public static string For(int orderId) => $"{Prefix}{orderId}-{RunId}";

    public static string IdempotencyKey(string action, int orderId) => $"{action}-order-{orderId}-{RunId}";

    /// <summary>Extracts the order id from a provider reference, or null if it is not one of ours.</summary>
    public static int? TryParseOrderId(string? reference)
    {
        if (string.IsNullOrEmpty(reference) || !reference.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = reference.Substring(Prefix.Length);
        var dash = rest.IndexOf('-');
        var idPart = dash >= 0 ? rest.Substring(0, dash) : rest;
        return int.TryParse(idPart, out var id) ? id : null;
    }
}
