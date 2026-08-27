using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Builds the deterministic, per-order idempotency keys and reference ids sent to PayPal.
/// A process-unique component keeps keys from colliding with earlier runs of the app
/// (the merchant account remembers invoice ids and PayPal-Request-Ids across runs, while
/// the in-memory store resets order ids). Within a run, the key for a given order action
/// is stable, so a retried request is deduplicated by PayPal.
/// </summary>
public static class PaymentKeys
{
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    public static string ReferenceId(int orderId) => $"eshop-{RunId}-order-{orderId}";
    public static string AuthorizeKey(int orderId) => $"{ReferenceId(orderId)}-authorize";
    public static string CaptureKey(int orderId) => $"{ReferenceId(orderId)}-capture";
    public static string VoidKey(int orderId) => $"{ReferenceId(orderId)}-void";
    public static string ReauthorizeKey(int orderId) => $"{ReferenceId(orderId)}-reauthorize";
    public static string RefundKey(string idempotencyKey) => $"eshop-{RunId}-refund-{idempotencyKey}";
}
