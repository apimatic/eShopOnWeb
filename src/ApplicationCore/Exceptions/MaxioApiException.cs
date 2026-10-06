using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success HTTP status.
/// Carries the provider's validation messages so they can be surfaced to callers.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> ProviderErrors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string>? providerErrors, string? body)
        : base(BuildMessage(statusCode, providerErrors, body))
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    private static string BuildMessage(int statusCode, IReadOnlyList<string>? providerErrors, string? body)
    {
        if (providerErrors is { Count: > 0 })
        {
            return $"Maxio Advanced Billing responded with HTTP {statusCode}: {string.Join("; ", providerErrors)}";
        }

        return $"Maxio Advanced Billing responded with HTTP {statusCode}. {body}";
    }
}
