namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// Maxio Advanced Billing subscription states. Live/problem states still
/// represent an existing enrollment that a double-click must not duplicate.
/// </summary>
public static class SubscriptionStates
{
    public static bool RepresentsExistingEnrollment(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return state is
            "active" or
            "trialing" or
            "assessing" or
            "pending" or
            "paused" or
            "past_due" or
            "soft_failure" or
            "unpaid" or
            "on_hold" or
            "suspended";
    }
}
