using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base(Format(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    private static string Format(int statusCode, IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
        {
            return $"Maxio Billing API request failed with HTTP {statusCode}.";
        }

        return $"Maxio Billing API request failed with HTTP {statusCode}: {string.Join("; ", errors)}";
    }
}
