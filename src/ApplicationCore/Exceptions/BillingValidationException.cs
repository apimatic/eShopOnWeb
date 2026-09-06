using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The request was rejected as invalid, either by the billing provider or before it was sent. The
/// caller has to change something for it to succeed, so this surfaces as a client error rather than
/// an upstream fault.
/// </summary>
public class BillingValidationException : BillingProviderException
{
    public BillingValidationException(string message, int? statusCode = null,
        IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, statusCode, errors, innerException)
    {
    }
}
