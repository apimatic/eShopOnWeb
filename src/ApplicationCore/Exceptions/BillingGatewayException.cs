using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects or cannot fulfill a request. Carries the provider's
/// HTTP status so callers can translate it into an appropriate client-facing response.
/// </summary>
public class BillingGatewayException : Exception
{
    public BillingGatewayException(int? statusCode, IReadOnlyList<string> errors)
        : base(errors is { Count: > 0 } ? string.Join(" ", errors) : "The billing provider returned an error.")
    {
        StatusCode = statusCode;
        Errors = errors ?? new List<string>();
    }

    /// <summary>
    /// HTTP status returned by the billing provider, when available.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Human readable errors returned by the billing provider.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
}
