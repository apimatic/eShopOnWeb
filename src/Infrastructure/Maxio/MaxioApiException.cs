using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Represents a non-success response returned by the Maxio Advanced Billing API.
/// Carries the HTTP status code and the error message(s) surfaced by the API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(BuildMessage(message, errors))
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public MaxioApiException(HttpStatusCode statusCode, string message, IReadOnlyList<string>? errors = null)
        : this((int)statusCode, message, errors)
    {
    }

    /// <summary>The HTTP status code returned by the Maxio API.</summary>
    public int StatusCode { get; }

    /// <summary>The individual error messages returned by the Maxio API.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the API rejected the request because a reference value (customer or subscription)
    /// is already in use. Used to implement idempotent create-or-get semantics.
    /// </summary>
    public bool IsReferenceConflict =>
        StatusCode == (int)HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e =>
            e.IndexOf("reference", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (e.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0 ||
             e.IndexOf("taken", StringComparison.OrdinalIgnoreCase) >= 0));

    private static string BuildMessage(string fallback, IReadOnlyList<string>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return fallback;
        }

        return string.Join(" ", errors);
    }
}

/// <summary>
/// Thrown when a shopper asks to subscribe to a plan that is not part of the configured
/// Maxio product family (or no longer exists).
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"The subscription plan '{productHandle}' is not available in the configured product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
