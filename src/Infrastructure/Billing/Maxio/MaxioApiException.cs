using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success answer from Maxio, carrying everything the calling service needs to decide what it
/// means in domain terms. Never leaves the Infrastructure layer.
/// </summary>
internal sealed class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpMethodAndPath request,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        string? rawBody,
        string? requestId)
        : base(BuildMessage(request, statusCode, errors, rawBody))
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
        RequestId = requestId;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Individual messages parsed out of the Maxio error envelope; empty when it had none.</summary>
    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }

    /// <summary>Value of the Maxio <c>X-Request-Id</c> response header, for provider-side correlation.</summary>
    public string? RequestId { get; }

    private static string BuildMessage(HttpMethodAndPath request, HttpStatusCode statusCode, IReadOnlyList<string> errors, string? rawBody)
    {
        var detail = errors.Count > 0
            ? string.Join("; ", errors)
            : Truncate(rawBody);

        return $"Maxio {request.Method} {request.Path} returned {(int)statusCode} {statusCode}" +
               (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}");
    }

    private static string? Truncate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        const int limit = 500;
        return body.Length <= limit ? body : body[..limit] + "...";
    }
}

/// <summary>Identifies the call that failed, without exposing credentials or query values.</summary>
internal readonly record struct HttpMethodAndPath(string Method, string Path);
