using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system did not answer in time (<see cref="TimedOut"/>) or could not be reached at all.
/// </summary>
public class BillingProviderUnavailableException : Exception
{
    public BillingProviderUnavailableException(string message, bool timedOut, Exception? innerException = null)
        : base(message, innerException)
    {
        TimedOut = timedOut;
    }

    public bool TimedOut { get; }
}
