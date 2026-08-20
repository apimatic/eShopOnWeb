using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public static class SubscriptionStates
{
    public static bool IsOpen(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return state.ToLowerInvariant() switch
        {
            "canceled" or "cancelled" or "expired" or "failed_to_create" or "trial_ended" => false,
            _ => true
        };
    }
}
