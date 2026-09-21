using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// A process-unique id generated once per run. It scopes the reconciliation keys (PayPal purchase-unit
/// custom_id / invoice_id) and the deterministic idempotency seeds so ids are unique across runs — which
/// matters because the in-memory store restarts empty each run and order ids restart at 1, while PayPal's
/// records persist. Registered as a singleton.
/// </summary>
public sealed class PaymentRunContext
{
    /// <summary>32-char hex id, unique to this process run.</summary>
    public string RunId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Reconciliation match key echoed to PayPal as the purchase-unit custom_id.</summary>
    public string CustomId(int orderId) => $"{RunId}:{orderId}";

    /// <summary>Unique-per-run invoice id echoed to PayPal as the purchase-unit invoice_id.</summary>
    public string InvoiceId(int orderId) => $"{RunId}-{orderId}";

    /// <summary>Deterministic seed for the authorize step's PayPal-Request-Id keys.</summary>
    public string AuthorizeSeed(int orderId) => $"{RunId}-{orderId}";

    /// <summary>Deterministic PayPal-Request-Id for the capture of an order.</summary>
    public string CaptureKey(int orderId) => $"capture-{RunId}-{orderId}";

    /// <summary>Deterministic PayPal-Request-Id for a re-authorization of an order.</summary>
    public string ReauthorizeKey(int orderId) => $"reauth-{RunId}-{orderId}";

    /// <summary>Deterministic PayPal-Request-Id for the void of an order.</summary>
    public string VoidKey(int orderId) => $"void-{RunId}-{orderId}";

    /// <summary>
    /// PayPal-Request-Id for a refund. Combines the run id, order id and the caller's idempotency key so the
    /// value sent to PayPal is globally unique per run (PayPal rejects a reused PayPal-Request-Id merchant-wide),
    /// while our own dedup keys off the caller's raw key. A repeat caller key never reaches PayPal twice because
    /// the service returns the already-stored refund first.
    /// </summary>
    public string RefundRequestId(int orderId, string callerKey) => $"refund-{RunId}-{orderId}-{callerKey}";

    /// <summary>
    /// Parses the eShop order id back out of a PayPal transaction's custom_id ("{runId}:{orderId}").
    /// Returns null when the value is absent or not one of ours.
    /// </summary>
    public static int? TryParseOrderIdFromCustomId(string? customId)
    {
        if (string.IsNullOrEmpty(customId))
        {
            return null;
        }
        var separator = customId!.LastIndexOf(':');
        if (separator < 0 || separator == customId.Length - 1)
        {
            return null;
        }
        return int.TryParse(customId.Substring(separator + 1), out var orderId) ? orderId : (int?)null;
    }
}
