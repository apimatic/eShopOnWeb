using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when the billing system of record cannot satisfy a request. Carries an HTTP-style
/// <see cref="StatusCode"/> so the presentation layer can translate it into an appropriate
/// response without depending on the billing SDK's own exception types.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status the presentation layer should surface (e.g. 404, 422, 502).</summary>
    public int StatusCode { get; }
}

/// <summary>Raised when a requested plan handle does not exist in the configured catalog.</summary>
public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' was found in the configured product family.", 404)
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
