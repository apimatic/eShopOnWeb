using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The app cannot carry out a billing request; carries the HTTP status and the reason for the caller.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    /// <summary>True when Maxio could not be reached or did not answer, so a write's outcome is unknown.</summary>
    public bool OutcomeUnknown { get; }

    public BillingException(int statusCode, string message, Exception? inner = null, bool outcomeUnknown = false) : base(message, inner)
    {
        StatusCode = statusCode;
        OutcomeUnknown = outcomeUnknown;
    }

    public static BillingException BadRequest(string message) => new(400, message);
    public static BillingException NotFound(string message) => new(404, message);
    public static BillingException Conflict(string message) => new(409, message);
}
