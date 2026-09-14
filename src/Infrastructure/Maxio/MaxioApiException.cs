using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, method, path, errors))
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public string Path { get; }

    /// <summary>Messages taken from the <c>errors</c> element of the response body, when present.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join(" ", errors) : "no error detail was returned";
        return $"Maxio API request {method} {path} failed with {(int)statusCode} {statusCode}: {detail}";
    }
}

/// <summary>
/// The API rejected the request on business rules (HTTP 422).
/// </summary>
public class MaxioValidationException : MaxioApiException
{
    public MaxioValidationException(string method, string path, IReadOnlyList<string> errors)
        : base(HttpStatusCode.UnprocessableEntity, method, path, errors)
    {
    }

    /// <summary>
    /// True when the request was refused because a record with the same reference already exists.
    /// The caller can reconcile with the existing record instead of failing.
    /// </summary>
    public bool IsDuplicateReference
    {
        get
        {
            foreach (var error in Errors)
            {
                if (error.Contains("must be unique", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// The API recognised the request as a duplicate submission (HTTP 409). An identical request was
/// already accepted, so the caller must reconcile rather than retry.
/// </summary>
public class MaxioDuplicateSubmissionException : MaxioApiException
{
    public MaxioDuplicateSubmissionException(string method, string path, IReadOnlyList<string> errors)
        : base(HttpStatusCode.Conflict, method, path, errors)
    {
    }
}
