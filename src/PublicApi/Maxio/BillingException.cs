using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The app cannot carry out a request; carries the status and the reason for the caller.
/// </summary>
public class BillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>True when a write may have been applied by Maxio although no answer was received.</summary>
    public bool OutcomeUnknown { get; }

    public BillingException(HttpStatusCode statusCode, string message, Exception? inner = null, bool outcomeUnknown = false)
        : base(message, inner)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }
}
