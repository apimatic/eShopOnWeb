using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public static class MaxioSubscriptionStates
{
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    public static bool IsEndOfLife(string? state) => state is not null && EndOfLifeStates.Contains(state);
}
