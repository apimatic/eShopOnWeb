using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IEnumerable<string> errors)
        : base(CreateMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors.Take(5).ToArray();
    }

    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool IsRetryable => StatusCode == 429 || StatusCode >= 500;

    private static string CreateMessage(int statusCode, IEnumerable<string> errors)
    {
        var safeErrors = errors.Where(error => !string.IsNullOrWhiteSpace(error)).Take(5).ToArray();
        return safeErrors.Length == 0
            ? $"Maxio Advanced Billing returned HTTP {statusCode}."
            : $"Maxio Advanced Billing returned HTTP {statusCode}: {string.Join("; ", safeErrors)}";
    }
}
