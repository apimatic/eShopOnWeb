using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The Maxio billing provider could not be reached, returned a server-side error, or returned a
/// response that could not be processed. The caller should treat this as a provider outage and
/// may retry later - it is not a defect in the caller's request.
/// </summary>
public class MaxioBillingUnavailableException : Exception
{
    public MaxioBillingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
