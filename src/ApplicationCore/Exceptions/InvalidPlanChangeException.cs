using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan change cannot be attempted — it is a no-op, or the subscription is not in a state
/// that allows a plan change (plan.md UC3 failure scenarios). No provider call is made.
/// </summary>
public class InvalidPlanChangeException : Exception
{
    public InvalidPlanChangeException(string message) : base(message)
    {
    }

    public static InvalidPlanChangeException SamePlan(string planHandle) =>
        new($"The subscription is already on plan '{planHandle}'; there is nothing to change.");
}
