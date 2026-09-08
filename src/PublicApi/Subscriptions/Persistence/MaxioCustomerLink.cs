using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Persistence;

/// <summary>
/// Maps an eShopOnWeb application user to the Maxio customer that represents them in the
/// billing system of record. This is a local cache / index used to keep Maxio calls cheap and
/// idempotent; Maxio (customer.reference) remains the durable source of truth.
/// </summary>
public class MaxioCustomerLink
{
    public int Id { get; set; }

    /// <summary>The ASP.NET Identity user id (GUID).</summary>
    public string AppUserId { get; set; } = string.Empty;

    /// <summary>The id of the corresponding Maxio customer.</summary>
    public int MaxioCustomerId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
