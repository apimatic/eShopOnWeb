using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested subscription plan is not published by the billing provider.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string message) : base(message)
    {
    }

    public static SubscriptionPlanNotFoundException ForHandle(string handle, IEnumerable<string> availableHandles) =>
        new($"Subscription plan '{handle}' was not found. Available plans: {Describe(availableHandles)}.");

    /// <summary>Lists the handles a caller may choose from, for use in an error message.</summary>
    public static string Describe(IEnumerable<string> availableHandles)
    {
        var available = string.Join(", ", availableHandles);

        return string.IsNullOrEmpty(available) ? "none are currently published" : available;
    }
}
