using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected the request as invalid (HTTP 422). Retrying the same request
/// unchanged will fail the same way, so the messages are surfaced to the caller.
/// </summary>
public class BillingValidationException : BillingProviderException
{
    public BillingValidationException(string message, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, 422, errors, innerException)
    {
    }
}
