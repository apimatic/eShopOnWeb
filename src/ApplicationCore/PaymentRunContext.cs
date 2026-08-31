using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Per-process uniqueness scope for PayPal invoice ids and PayPal-Request-Id headers.
/// PayPal caches responses by request id and enforces globally unique invoice ids per
/// merchant; when the store runs on the in-memory database, local ids (order ids) reset
/// on every process start, so ids derived only from them collide with earlier runs and
/// PayPal replays stale cached responses. Scoping those ids to the process run keeps
/// them stable within a run (so genuine retries are deduped) but unique across runs.
/// </summary>
public static class PaymentRunContext
{
    public static readonly string RunId = Guid.NewGuid().ToString("N")[..8];
}
