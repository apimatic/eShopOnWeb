using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlanChangePreviewEntry(int SubscriptionId, string FromProductHandle, string ToProductHandle, bool ApplyAtRenewal, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Short-lived, in-memory store of plan-change previews keyed by a one-time token. Backs the "preview
/// must match commit" guarantee in UC3: a commit is only honored while its preview token is present
/// and unexpired, so a stale or replayed commit is rejected rather than silently re-priced.
/// </summary>
public interface IPlanChangePreviewCache
{
    Guid Store(PlanChangePreviewEntry entry);

    /// <summary>Removes and returns the entry for <paramref name="token"/> if it exists and has not expired; otherwise null.</summary>
    PlanChangePreviewEntry? TryConsume(Guid token);
}
