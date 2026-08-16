using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// A per-process identifier. Combined with the order id it yields references (custom_id / invoice_id /
/// idempotency keys) that stay stable within a run — so a double-click is idempotent — yet differ across
/// runs, which matters because the in-memory store reuses order ids and PayPal requires unique invoice ids.
/// </summary>
public class PaymentInstance
{
    public string RunId { get; }

    public PaymentInstance()
    {
        RunId = Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    public PaymentInstance(string runId)
    {
        RunId = runId;
    }
}
