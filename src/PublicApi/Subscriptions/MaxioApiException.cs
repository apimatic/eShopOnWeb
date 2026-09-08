using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? responseBody = null)
        : base(BuildMessage(statusCode, errors, responseBody))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors, string? responseBody)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : responseBody ?? "unknown error";
        return $"Maxio Advanced Billing request failed with status {statusCode}: {detail}";
    }
}
