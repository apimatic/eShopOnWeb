using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio call failed. Carries the provider's status code and any error messages it returned,
/// modelled on the specification's error responses (<c>Error-List-Response</c> and friends).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        string message,
        HttpStatusCode? statusCode = null,
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public HttpStatusCode? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected the request itself (<c>422</c>) rather than failing to serve it.
    /// </summary>
    public bool IsValidationFailure => StatusCode == HttpStatusCode.UnprocessableEntity;
}
