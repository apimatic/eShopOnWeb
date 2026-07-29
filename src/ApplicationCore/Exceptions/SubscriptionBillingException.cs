using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Classifies a subscription-billing failure so the API layer can map it to an HTTP status
/// without knowing anything about the underlying billing provider.
/// </summary>
public enum SubscriptionBillingError
{
    /// <summary>The billing integration is not configured (missing credentials/settings).</summary>
    NotConfigured,

    /// <summary>The request was invalid (e.g. missing/unknown plan handle).</summary>
    Validation,

    /// <summary>A referenced resource was not found.</summary>
    NotFound,

    /// <summary>The request conflicts with current state (e.g. a duplicate submission in flight).</summary>
    Conflict,

    /// <summary>The billing provider returned an unexpected error.</summary>
    Upstream
}

/// <summary>
/// Raised for any failure while talking to the subscription-billing provider.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        string message,
        SubscriptionBillingError error = SubscriptionBillingError.Upstream,
        IReadOnlyList<string>? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        Details = details ?? Array.Empty<string>();
    }

    /// <summary>The kind of failure, used to select an HTTP status code.</summary>
    public SubscriptionBillingError Error { get; }

    /// <summary>Additional messages (e.g. provider validation errors) to surface to the caller.</summary>
    public IReadOnlyList<string> Details { get; }
}
