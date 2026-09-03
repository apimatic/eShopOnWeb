using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not be reached, timed out, or returned a response that could not be
/// processed. The outcome carries no meaningful caller status, so it is surfaced as 502 Bad Gateway.
/// </summary>
public sealed class BillingUnavailableException : BillingException
{
    public BillingUnavailableException(string message, Exception? innerException = null)
        : base(message, (int)HttpStatusCode.BadGateway, innerException)
    {
    }
}
