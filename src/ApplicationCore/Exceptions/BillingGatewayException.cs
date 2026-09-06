using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system could not be reached, or answered with something we cannot act on.
/// Surfaces to callers as a gateway error - the shopper's request was not fulfilled.
/// </summary>
public class BillingGatewayException : Exception
{
    public BillingGatewayException(string message) : this(message, null, null, null)
    {
    }

    public BillingGatewayException(string message, Exception? innerException)
        : this(message, innerException, null, null)
    {
    }

    public BillingGatewayException(string message,
        Exception? innerException,
        int? statusCode,
        IReadOnlyCollection<string>? errors) : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by the billing system, when the call got that far.</summary>
    public int? StatusCode { get; }

    /// <summary>Validation messages reported by the billing system, if any.</summary>
    public IReadOnlyCollection<string> Errors { get; }

    public static BillingGatewayException FromResponse(string operation,
        int statusCode,
        IReadOnlyCollection<string> errors)
    {
        var detail = errors.Any() ? string.Join("; ", errors) : "no error detail was returned";
        return new BillingGatewayException(
            $"The billing system rejected '{operation}' with status {statusCode}: {detail}.",
            null, statusCode, errors);
    }
}
