using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the references and idempotency keys sent to PayPal. Every deterministic value is namespaced
/// with a per-process run id so that idempotency keys and order references are globally unique — a
/// resettable eShop order id (e.g. the in-memory store starts again at 1 each run) must never collide
/// with a previous run against the same PayPal account, which would make PayPal replay a stale capture
/// or reject an idempotency key as a duplicate. Registered as a singleton.
/// </summary>
public sealed class PaymentReferenceFactory
{
    /// <summary>Stable for the lifetime of the process; distinguishes this run's references from any other.</summary>
    public string RunId { get; } = Guid.NewGuid().ToString("N").Substring(0, 12);

    /// <summary>custom_id stamped on the PayPal purchase unit — the reconciliation join key back to the order.</summary>
    public string CustomId(int orderId) => $"{RunId}-{orderId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>invoice_id stamped on the PayPal purchase unit; unique per authorization attempt.</summary>
    public string InvoiceId(int orderId) => $"eshop-{RunId}-{orderId}-{Guid.NewGuid():N}";

    /// <summary>PayPal-Request-Id for creating+authorizing the order; unique per attempt (allows a retry after a decline).</summary>
    public string AuthorizeRequestId() => Guid.NewGuid().ToString("N");

    /// <summary>PayPal-Request-Id for the capture; stable per order so a retry dedupes, unique across runs.</summary>
    public string CaptureRequestId(int orderId) => $"cap-{RunId}-{orderId}";

    /// <summary>PayPal-Request-Id for a refund; stable per caller key so a repeat dedupes, unique across runs.</summary>
    public string RefundRequestId(string callerKey) => $"ref-{RunId}-{callerKey}";

    /// <summary>Recovers the eShop order id from a transaction's custom_field, but only for this run's references.</summary>
    public int? TryGetOrderId(string? customField)
    {
        if (string.IsNullOrWhiteSpace(customField)) return null;
        var prefix = RunId + "-";
        if (!customField.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return int.TryParse(customField.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id : null;
    }
}
