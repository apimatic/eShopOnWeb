using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The app cannot carry out a request; carries the HTTP status to answer with and a safe reason.
/// </summary>
public class BillingException : Exception
{
    public int Status { get; }

    /// <summary>True when a write may have reached Maxio but its result could not be read.</summary>
    public bool OutcomeUnknown { get; }

    public BillingException(int status, string message, bool outcomeUnknown = false, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
        OutcomeUnknown = outcomeUnknown;
    }
}
