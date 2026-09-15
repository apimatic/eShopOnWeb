using System;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the reference we send to PayPal as an order's invoice_id. PayPal requires invoice_id to be
/// unique per merchant account, but the in-memory database restarts order ids from 1 on every run,
/// so the reference is namespaced by a per-process run id. That id is stable for the life of the
/// process, which makes a given order's reference (and its derived idempotency keys) stable across a
/// double-click within one run, while staying unique across runs.
/// </summary>
public static class PaymentReference
{
    /// <summary>A short, per-process token that distinguishes this run's references from other runs'.</summary>
    public static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    public static string ForOrder(int orderId) => $"ESHOP-{RunId}-{orderId}";

    public static string AuthorizeKey(int orderId) => $"auth-{RunId}-{orderId}";

    public static string CaptureKey(int orderId) => $"cap-{RunId}-{orderId}";

    public static string ReauthorizeKey(int orderId) => $"reauth-{RunId}-{orderId}";

    /// <summary>Namespaces a caller's refund idempotency key so it cannot collide across orders at PayPal.</summary>
    public static string RefundKey(string reference, string callerKey) => $"refund-{reference}-{callerKey}";
}
