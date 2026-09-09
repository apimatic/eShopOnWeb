using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string message)
        : base(errors.Count > 0 ? $"{message} Errors: {string.Join(" | ", errors)}" : message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}
