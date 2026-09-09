using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Error raised when the Maxio Advanced Billing API returns a non-success response.
/// Parses the error models defined in maxio-spec/openapi.yaml (an "errors" array of
/// strings or an "errors" object of field/message pairs).
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string body, IReadOnlyList<string> errors)
        : base($"Maxio API request failed with HTTP {(int)statusCode} ({statusCode}). Errors: {(errors.Count > 0 ? string.Join(" | ", errors) : body)}")
    {
        StatusCode = statusCode;
        ResponseBody = body;
        Errors = errors;
    }

    public int StatusCode { get; }

    public string ResponseBody { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when the failure is caused by a duplicate unique value (e.g. a reference already taken).</summary>
    public bool IsDuplicateReferenceError()
    {
        foreach (var error in Errors)
        {
            if (error != null &&
                error.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                (error.Contains("already been taken", StringComparison.OrdinalIgnoreCase) ||
                 error.Contains("must be unique", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }
}
