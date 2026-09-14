using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Thrown when a Maxio Advanced Billing API call fails.</summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, IReadOnlyList<string> errors, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int StatusCode { get; }

    /// <summary>Raw error list returned by Maxio (empty when none).</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool IsReferenceConflict => StatusCode == 422 &&
        Errors.Any(e => e != null && e.Contains("Reference", StringComparison.OrdinalIgnoreCase));
}
