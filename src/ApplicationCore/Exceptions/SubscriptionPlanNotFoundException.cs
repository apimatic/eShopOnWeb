using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller asks to subscribe to a plan handle the billing catalog does not publish.
/// This is a caller error, not a billing outage.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string? requestedHandle, IEnumerable<string> availableHandles)
        : base(BuildMessage(requestedHandle, availableHandles.ToList()))
    {
        RequestedHandle = requestedHandle;
        AvailableHandles = availableHandles.ToList();
    }

    public string? RequestedHandle { get; }

    public IReadOnlyList<string> AvailableHandles { get; }

    private static string BuildMessage(string? requestedHandle, IReadOnlyList<string> available)
    {
        var known = available.Count == 0 ? "(none published)" : string.Join(", ", available);
        return string.IsNullOrWhiteSpace(requestedHandle)
            ? $"No subscription plan was specified and no default plan is configured. Available plans: {known}."
            : $"Subscription plan '{requestedHandle}' was not found. Available plans: {known}.";
    }
}
