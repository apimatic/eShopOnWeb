using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Base type for expected, caller-actionable subscription errors.
/// </summary>
public abstract class SubscriptionException : Exception
{
    protected SubscriptionException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// The requested plan handle does not exist in the configured product family.
/// Maps to HTTP 400.
/// </summary>
public sealed class UnknownPlanException : SubscriptionException
{
    public UnknownPlanException(string? requestedHandle, IEnumerable<string> availableHandles)
        : base(BuildMessage(requestedHandle, availableHandles))
    {
        RequestedHandle = requestedHandle;
        AvailableHandles = availableHandles.ToArray();
    }

    public string? RequestedHandle { get; }
    public IReadOnlyList<string> AvailableHandles { get; }

    private static string BuildMessage(string? requestedHandle, IEnumerable<string> availableHandles)
    {
        var available = string.Join(", ", availableHandles);
        return string.IsNullOrWhiteSpace(requestedHandle)
            ? $"A plan handle is required. Available plans: {available}."
            : $"Unknown plan handle '{requestedHandle}'. Available plans: {available}.";
    }
}

/// <summary>
/// The billing system could not fulfil the request (transport error, non-success response,
/// or an error payload returned by Maxio). Maps to HTTP 502.
/// </summary>
public sealed class SubscriptionBillingException : SubscriptionException
{
    public SubscriptionBillingException(string message, Exception? inner = null)
        : base(message, inner) { }
}
