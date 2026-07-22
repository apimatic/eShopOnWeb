using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects or fails a call. Carries the provider's own status
/// code and messages so callers can surface them rather than a generic failure.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string operation, int statusCode, IReadOnlyCollection<string> errors)
        : base(BuildMessage(operation, statusCode, errors))
    {
        Operation = operation;
        StatusCode = statusCode;
        Errors = errors;
    }

    public BillingProviderException(string operation, string message, Exception innerException)
        : base($"Billing provider call '{operation}' failed: {message}", innerException)
    {
        Operation = operation;
        StatusCode = 0;
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// The integration operation that failed, e.g. "CreateSubscription".
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// The provider's HTTP status code, or 0 when the call never produced a response.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The provider's own error messages, empty when it did not supply any.
    /// </summary>
    public IReadOnlyCollection<string> Errors { get; }

    private static string BuildMessage(string operation, int statusCode, IReadOnlyCollection<string> errors)
    {
        var detail = errors.Any() ? string.Join("; ", errors) : "no error detail supplied";
        return $"Billing provider call '{operation}' failed with status {statusCode}: {detail}";
    }
}
