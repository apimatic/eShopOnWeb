using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures originating from the subscription billing capability.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
    }

    public BillingException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when a requested plan handle does not correspond to an available plan.
/// Maps to an HTTP 404 at the API boundary.
/// </summary>
public sealed class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No available subscription plan was found for handle '{planHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Raised when the billing system of record returns an error or is unreachable.
/// Maps to an HTTP 502 at the API boundary. <see cref="StatusCode"/> is the upstream
/// HTTP status when one was received, otherwise null.
/// </summary>
public sealed class BillingUpstreamException : BillingException
{
    public BillingUpstreamException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
