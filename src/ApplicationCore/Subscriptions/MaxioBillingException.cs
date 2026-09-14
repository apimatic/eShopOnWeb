using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when the Maxio API rejects a request or is unreachable. Carries an
/// optional upstream HTTP status so the API layer can translate it faithfully.
/// </summary>
public class MaxioBillingException : Exception
{
    /// <summary>Upstream Maxio HTTP status code, when the failure originated from a response.</summary>
    public int? UpstreamStatusCode { get; }

    public MaxioBillingException(string message, int? upstreamStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
    }
}

/// <summary>
/// Raised when a requested plan handle is not present in the configured product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
