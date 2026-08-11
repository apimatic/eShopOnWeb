using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// A per-process identifier. Because the in-memory database resets on restart (and order ids
/// restart from 1), this id is mixed into PayPal idempotency keys and invoice ids so that a new
/// run never collides with a previous run's PayPal-Request-Id / invoice_id on the merchant account.
/// </summary>
public static class RuntimeContext
{
    public static string InstanceId { get; } = Guid.NewGuid().ToString("N").Substring(0, 12);
}
