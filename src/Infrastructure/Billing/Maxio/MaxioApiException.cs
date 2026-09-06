using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio could not be reached, or answered with a status this application cannot act on.
/// </summary>
public class MaxioApiException : BillingProviderException
{
    public MaxioApiException(string message, int? statusCode = null,
        IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, statusCode, errors, innerException)
    {
    }
}

/// <summary>
/// Maxio rejected the request as invalid (the specification's <c>422</c> error responses).
/// </summary>
public class MaxioValidationException : BillingValidationException
{
    public MaxioValidationException(string message, int? statusCode = null,
        IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, statusCode, errors, innerException)
    {
    }
}
