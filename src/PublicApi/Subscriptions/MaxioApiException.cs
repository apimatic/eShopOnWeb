using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, string? errorDetail)
        : base(BuildMessage(statusCode, operation, errorDetail))
    {
        StatusCode = statusCode;
        Operation = operation;
    }

    public HttpStatusCode StatusCode { get; }
    public string Operation { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string operation, string? errorDetail)
    {
        var suffix = string.IsNullOrWhiteSpace(errorDetail) ? string.Empty : $" {errorDetail}";
        return $"Maxio operation '{operation}' returned HTTP {(int)statusCode}.{suffix}";
    }
}
