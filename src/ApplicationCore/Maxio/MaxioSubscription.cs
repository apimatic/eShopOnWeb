using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioSubscription
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>
    /// True unless the subscription has reached a terminal (canceled/expired/failed) state,
    /// used to decide whether re-subscribing to the same plan should be a no-op.
    /// </summary>
    public bool IsEnrolled => !TerminalStates.Contains(State);
}
