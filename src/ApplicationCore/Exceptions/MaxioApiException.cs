using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a call to the Maxio Advanced Billing API fails.</summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors) =>
        $"Maxio API request failed with status {(int)statusCode}: {string.Join("; ", errors.DefaultIfEmpty("(no error details returned)"))}";
}
