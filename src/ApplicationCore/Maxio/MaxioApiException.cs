using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, errors, rawBody))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors, string? rawBody)
    {
        if (errors.Count > 0)
            return $"HTTP Response Not OK. Status code: {statusCode}. Response: '{{{string.Join(", ", errors)}}}'";

        return $"HTTP Response Not OK. Status code: {statusCode}. Response: '{rawBody}'";
    }

    /// <summary>
    /// True when Maxio rejected a create because the supplied reference is already in use
    /// (the spec enforces reference uniqueness for customers and subscriptions).
    /// </summary>
    public bool IsReferenceUniquenessViolation()
    {
        if (StatusCode != 422)
            return false;

        return Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && (e.Contains("unique", StringComparison.OrdinalIgnoreCase) || e.Contains("taken", StringComparison.OrdinalIgnoreCase)));
    }
}
